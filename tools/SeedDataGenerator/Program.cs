using System.Text;
using System.Text.Json;
using SeedDataGenerator;
using VibrationDashboard.DataSources.Json;

// ──────────────────────────────────────────────────────────────────────────
// 測試資料產生器(Phase 2):程式化產生 15 份擬真振動 JSON(一機一檔),
// 確定性涵蓋「良好 / 異常 / 危險」三種樣態,並內嵌自繪 PNG 佔位圖(二位元圖檔)。
// 用法:dotnet run --project tools/SeedDataGenerator -- <輸出資料夾>
//   未指定輸出資料夾時,預設寫到目前工作目錄下的 App_Data/machines。
// 特性:固定亂數種子 + 固定錨定日期 → 每次產出穩定,利於版控與 demo。
// ──────────────────────────────────────────────────────────────────────────

var outputDir = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "machines");

Directory.CreateDirectory(outputDir);

// 固定錨定:最後一筆量測時間(7/1);往前每日一筆共 40 筆(40 天,讓「7天/30天/全部」區間有意義差異)。
var anchor = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Unspecified);
const int pointCount = 40;
var rnd = new Random(20260623); // 固定種子 → 可重現

var specs = new MachineSpec[]
{
    new("PUMP-A01", "冷卻水泵 A01", "泵浦",   0.50, 0.88, Profile.Abnormal),
    new("PUMP-A02", "冷卻水泵 A02", "泵浦",   0.50, 0.88, Profile.Good),
    new("PUMP-A03", "冷卻水泵 A03", "泵浦",   0.50, 0.88, Profile.Danger),
    new("FAN-B01",  "送風機 B01",   "風機",   1.00, 1.80, Profile.Good),
    new("FAN-B02",  "抽風機 B02",   "風機",   1.00, 1.80, Profile.Danger),
    new("MOTOR-C01","主馬達 C01",   "馬達",   0.71, 1.12, Profile.Good),
    new("MOTOR-C02","主馬達 C02",   "馬達",   0.71, 1.12, Profile.Abnormal),
    new("GEAR-D01", "齒輪箱 D01",   "齒輪箱", 1.80, 4.50, Profile.Abnormal),
    new("GEAR-D02", "齒輪箱 D02",   "齒輪箱", 1.80, 4.50, Profile.Good),
    new("COMP-E01", "空壓機 E01",   "空壓機", 1.12, 2.80, Profile.Danger),
    new("SPDL-F01", "精密主軸 F01", "主軸",   0.11, 0.18, Profile.Good),
    new("SPDL-F02", "精密主軸 F02", "主軸",   0.11, 0.18, Profile.Abnormal),
    new("BLOW-G01", "鼓風機 G01",   "鼓風機", 1.00, 1.80, Profile.Good),
    new("BEAR-H01", "軸承座 H01",   "軸承座", 0.45, 0.71, Profile.Danger),
    new("CHLR-I01", "冰水主機 I01", "冰水主機", 0.71, 1.12, Profile.Abnormal),
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    // camelCase 對齊 docs/03 的檔案 schema(id/name/warningThreshold...)。
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    // 中文名稱不轉成 \uXXXX,輸出可讀的 UTF-8。
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

var statusCount = new Dictionary<string, int> { ["Good"] = 0, ["Abnormal"] = 0, ["Danger"] = 0 };

for (var i = 0; i < specs.Length; i++)
{
    var spec = specs[i];
    var values = GenerateSeries(spec.Profile, spec.Warning, spec.Danger, rnd);

    var measurements = new List<MeasurementFileModel>(pointCount);
    for (var p = 0; p < pointCount; p++)
    {
        measurements.Add(new MeasurementFileModel
        {
            Time = anchor.AddDays(-(pointCount - 1 - p)),
            Value = Math.Round(values[p], 3)
        });
    }

    var derivedStatus = EvaluateStatus(values[^1], spec.Warning, spec.Danger);
    statusCount[derivedStatus]++;

    // 設備卡片圖:工業漸層 + 振動波形圖騰(逐台微變化,中性不帶狀態色)。
    var png = SolidPngEncoder.CreateMachineCard(320, 180, i);
    var imageDataUri = "data:image/png;base64," + Convert.ToBase64String(png);

    var model = new MachineFileModel
    {
        Id = spec.Id,
        Name = spec.Name,
        Type = spec.Type,
        Unit = "mm/s",
        WarningThreshold = spec.Warning,
        DangerThreshold = spec.Danger,
        Status = derivedStatus, // 寫入以利人工檢視;讀取端忽略(衍生值)
        Image = imageDataUri,
        Measurements = measurements
    };

    WriteAtomic(Path.Combine(outputDir, spec.Id + ".json"), JsonSerializer.Serialize(model, jsonOptions));
    Console.WriteLine($"  OK {spec.Id,-10} {derivedStatus,-8} latest={values[^1]:0.000} {spec.Name}");
}

Console.WriteLine();
Console.WriteLine($"已產生 {specs.Length} 份 JSON 至：{outputDir}");
Console.WriteLine($"狀態分布 → 良好 {statusCount["Good"]} / 異常 {statusCount["Abnormal"]} / 危險 {statusCount["Danger"]}");
return 0;

// ── 量測序列產生:依樣態生成 pointCount 筆(40 天),並把「最後一筆」鎖進目標區間,確保狀態確定 ──
double[] GenerateSeries(Profile profile, double w, double d, Random r)
{
    var v = new double[pointCount];
    double Noise(double amp) => (r.NextDouble() - 0.5) * 2.0 * amp;

    switch (profile)
    {
        case Profile.Good:
            // 平穩低檔:圍繞 w*0.35,輕微抖動;最後一筆夾在 w 以下。
            for (var i = 0; i < pointCount; i++)
                v[i] = Clamp(w * 0.35 + Noise(w * 0.10), w * 0.05, w * 0.80);
            v[^1] = Clamp(v[^1], w * 0.05, w * 0.82);
            break;

        case Profile.Abnormal:
            // 緩升:由 w*0.55 線性升到 (w..d) 中段;最後一筆鎖在 [w, d)。
            GenerateRamp(v, w * 0.55, w + (d - w) * 0.50, Noise, (d - w) * 0.06);
            v[^1] = Clamp(v[^1], w * 1.02, w + (d - w) * 0.92);
            break;

        case Profile.Danger:
            // 惡化:由 w*0.75 升越危險線;最後一筆鎖在 d 之上。
            GenerateRamp(v, w * 0.75, d * 1.10, Noise, (d - w) * 0.08);
            v[^1] = Math.Max(v[^1], d * 1.05);
            break;
    }

    for (var i = 0; i < pointCount; i++)
        v[i] = Math.Max(0, v[i]);
    return v;
}

void GenerateRamp(double[] v, double start, double end, Func<double, double> noise, double amp)
{
    for (var i = 0; i < pointCount; i++)
    {
        var t = (double)i / (pointCount - 1);
        v[i] = start + (end - start) * t + noise(amp);
    }
}

static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);

static string EvaluateStatus(double latest, double w, double d)
    => latest >= d ? "Danger" : latest >= w ? "Abnormal" : "Good";

// 原子寫檔:先寫 .tmp 再覆蓋,避免讀端讀到寫一半的檔(對應 docs/03 §3 寫入安全)。
static void WriteAtomic(string targetPath, string content)
{
    var tmp = targetPath + ".tmp";
    File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    File.Move(tmp, targetPath, overwrite: true);
}

// ── 型別宣告(須置於所有 top-level 陳述式與區域函式之後)──
internal enum Profile { Good, Abnormal, Danger }

internal readonly record struct MachineSpec(string Id, string Name, string Type, double Warning, double Danger, Profile Profile);
