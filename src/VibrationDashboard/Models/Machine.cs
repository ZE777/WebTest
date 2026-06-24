namespace VibrationDashboard.Models;

/// <summary>
/// 設備（感測點）領域模型。為<b>資料來源中立</b>的模型：JSON / SQL 來源都映射到此型別。
/// 圖檔一律以二位元（<see cref="ImageBytes"/>）表示，不帶 base64 字串；
/// 狀態為衍生值，由 <c>StatusEvaluator</c> 依最新值對門檻判定，故此型別<b>不存 Status</b>。
/// </summary>
public sealed class Machine
{
    /// <summary>識別碼（JSON 路徑下由檔名衍生，一機一檔）。</summary>
    public required string Id { get; init; }

    /// <summary>機器名稱。</summary>
    public required string Name { get; init; }

    /// <summary>設備類型（泵浦 / 風機 / 馬達…;選填,供分類顯示與篩選）。</summary>
    public string? Type { get; init; }

    /// <summary>量測單位（如 mm/s）。</summary>
    public string Unit { get; init; } = "mm/s";

    /// <summary>警告門檻（黃線）。逐台可設定（不同設備標準不同）。</summary>
    public double WarningThreshold { get; init; }

    /// <summary>危險門檻（紅線）。逐台可設定。</summary>
    public double DangerThreshold { get; init; }

    /// <summary>
    /// 機器圖檔（二位元）。看板初次載入<b>不夾帶</b>，改走獨立圖檔 API + ETag 快取（Phase 3）。
    /// </summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>圖檔 MIME 類型（如 image/png），供圖檔 API 設定 Content-Type。</summary>
    public string? ImageContentType { get; init; }

    /// <summary>量測序列（資料來源負責依時間遞增排序；通常 10 筆）。</summary>
    public IReadOnlyList<Measurement> Measurements { get; init; } = [];

    /// <summary>最新一筆量測（序列已遞增排序，取最後一筆）；無資料時為 null。</summary>
    public Measurement? Latest => Measurements.Count == 0 ? null : Measurements[^1];
}
