using System.Text.Json;
using Microsoft.Extensions.Options;
using VibrationDashboard.Common.Settings;
using VibrationDashboard.DataSources.Json;
using VibrationDashboard.Models;

namespace VibrationDashboard.DataSources;

/// <summary>
/// 主資料來源：讀取監看資料夾下的 JSON 檔（一機一檔），反序列化並映射為 <see cref="Machine"/>。
/// 圖檔 data URI 在此解析為二位元，讓領域模型不帶 base64 字串。
/// 單一檔損毀（如即時情境讀到寫一半的檔）只記錄並略過，不拖垮整體。
/// </summary>
public sealed class JsonFileDataSource : IMachineDataSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MachineDataOptions _options;
    private readonly ILogger<JsonFileDataSource> _logger;
    private readonly string _folder;

    public JsonFileDataSource(
        IOptions<MachineDataOptions> options,
        IHostEnvironment env,
        ILogger<JsonFileDataSource> logger)
    {
        _options = options.Value;
        _logger = logger;
        // 監看資料夾相對 ContentRoot 解析為絕對路徑（亦接受絕對路徑設定）。
        _folder = Path.IsPathRooted(_options.JsonFolder)
            ? _options.JsonFolder
            : Path.Combine(env.ContentRootPath, _options.JsonFolder);
    }

    /// <summary>讀取資料夾下所有 <c>*.json</c> 並映射為設備清單。</summary>
    public async Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder))
        {
            _logger.LogWarning("設備資料夾不存在：{Folder}", _folder);
            return [];
        }

        var machines = new List<Machine>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var machine = await ReadFileAsync(path, ct);
            if (machine is not null) machines.Add(machine);
        }
        return machines;
    }

    /// <summary>依識別碼讀取單一 <c>{id}.json</c>；不存在或 id 非法時回 <c>null</c>。</summary>
    public async Task<Machine?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (!IsValidId(id)) return null;
        var path = Path.Combine(_folder, id + ".json");
        return File.Exists(path) ? await ReadFileAsync(path, ct) : null;
    }

    private async Task<Machine?> ReadFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var model = await JsonSerializer.DeserializeAsync<MachineFileModel>(stream, JsonOptions, ct);
            return model is null ? null : MapToMachine(model, path);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON 解析失敗，略過檔案：{Path}", path);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "讀檔失敗，略過檔案：{Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            // 兜底:任何未預期的映射/解析意外(如欄位型別異常)都只略過單檔，
            // 不讓一個壞檔擊穿到全域 Middleware → 整份清單 500（貫徹本類別「壞檔不拖垮整體」承諾）。
            _logger.LogWarning(ex, "處理設備檔時發生未預期例外，略過檔案：{Path}", path);
            return null;
        }
    }

    private Machine MapToMachine(MachineFileModel model, string path)
    {
        // id 以「JSON 內的 id」為準，缺漏時退回檔名（一機一檔；檔名即識別碼）。
        var id = string.IsNullOrWhiteSpace(model.Id)
            ? Path.GetFileNameWithoutExtension(path)
            : model.Id;

        var (imageBytes, imageContentType) = ParseDataUri(model.Image);

        // 量測依時間遞增排序，讓 Machine.Latest 取最後一筆即為最新。
        // 防 P0-1：JSON 明寫 "measurements": null 時 System.Text.Json 會給 null → 以空集合兜底，避免 NRE。
        // 只保留最近 N 筆(MeasurementCount)：與 SQL 來源行為一致，未來單檔量測擴充時不會無上限成長。
        var ordered = (model.Measurements ?? [])
            .OrderBy(m => m.Time)
            .Select(m => new Measurement(m.Time, m.Value));
        var measurements = (_options.MeasurementCount > 0
            ? ordered.TakeLast(_options.MeasurementCount)
            : ordered).ToList();

        var (warning, danger) = NormalizeThresholds(model.WarningThreshold, model.DangerThreshold, path);

        return new Machine
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(model.Name) ? id : model.Name,
            Type = string.IsNullOrWhiteSpace(model.Type) ? null : model.Type.Trim(),
            Unit = string.IsNullOrWhiteSpace(model.Unit) ? _options.DefaultUnit : model.Unit,
            WarningThreshold = warning,
            DangerThreshold = danger,
            ImageBytes = imageBytes,
            ImageContentType = imageContentType,
            Measurements = measurements
        };
    }

    /// <summary>
    /// 門檻健全性檢查(P0-2):各自缺值(≤0)退回預設;若 <c>danger ≤ warning</c>(打反/相等)
    /// 則整組退回預設並記錄 —— 否則狀態判定會反轉(所有值直接判危險)、趨勢圖分區色帶畫出負高度。
    /// </summary>
    private (double warning, double danger) NormalizeThresholds(double warning, double danger, string path)
    {
        var w = warning > 0 ? warning : _options.DefaultWarningThreshold;
        var d = danger > 0 ? danger : _options.DefaultDangerThreshold;
        if (d <= w)
        {
            _logger.LogWarning(
                "門檻不合理(warning={Warning} >= danger={Danger})，退回預設({DefW}/{DefD})：{Path}",
                w, d, _options.DefaultWarningThreshold, _options.DefaultDangerThreshold, path);
            return (_options.DefaultWarningThreshold, _options.DefaultDangerThreshold);
        }
        return (w, d);
    }

    /// <summary>
    /// 圖檔 MIME 白名單(P0-4)。data URI 的 content-type 來自使用者可控的 JSON，
    /// 若原樣回給瀏覽器當 <c>Content-Type</c>，攻擊者可放 <c>data:text/html;base64,...</c>
    /// 讓圖檔端點回傳並執行任意 HTML/JS。故僅放行常見點陣圖格式；
    /// 刻意<b>排除 <c>image/svg+xml</c></b>(SVG 可內嵌 &lt;script&gt;)。
    /// </summary>
    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif"
    };

    /// <summary>
    /// 解析 data URI（<c>data:image/png;base64,xxxx</c>）為（位元組, MIME）。
    /// 非 data URI、格式錯誤、或 MIME 不在白名單時回 <c>(null, null)</c>（視同無圖）。
    /// </summary>
    private static (byte[]? bytes, string? contentType) ParseDataUri(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri)) return (null, null);

        const string prefix = "data:";
        if (!dataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return (null, null);

        var commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0) return (null, null);

        var meta = dataUri[prefix.Length..commaIndex];   // 例：image/png;base64
        var payload = dataUri[(commaIndex + 1)..];

        var isBase64 = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        var contentType = meta.Split(';')[0];
        // 不在白名單(含空字串、text/html、svg 等)一律拒圖 → 端點不會吐出可執行內容。
        if (!AllowedImageTypes.Contains(contentType)) return (null, null);

        try
        {
            var bytes = isBase64
                ? Convert.FromBase64String(payload)
                : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return (bytes, contentType);
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    /// <summary>識別碼白名單，避免路徑穿越：僅允許字母、數字、dash、底線。</summary>
    private static bool IsValidId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        foreach (var c in id)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_')) return false;
        }
        return true;
    }
}
