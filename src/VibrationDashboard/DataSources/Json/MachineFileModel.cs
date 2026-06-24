namespace VibrationDashboard.DataSources.Json;

/// <summary>
/// JSON 檔案結構契約（一機一檔 <c>{id}.json</c>，見 docs/03 §3）。
/// 同時供 <see cref="JsonFileDataSource"/> 讀取與測試資料產生器寫入，確保 schema <b>單一來源</b>。
/// </summary>
public sealed class MachineFileModel
{
    /// <summary>識別碼（一機一檔；通常等於檔名）。</summary>
    public string Id { get; set; } = "";

    /// <summary>機器名稱。</summary>
    public string Name { get; set; } = "";

    /// <summary>設備類型（如 泵浦 / 風機 / 馬達;選填,供分類顯示與搜尋,見 docs/07 §7）。</summary>
    public string? Type { get; set; }

    /// <summary>量測單位（如 mm/s）。</summary>
    public string Unit { get; set; } = "mm/s";

    /// <summary>警告門檻（黃線）。</summary>
    public double WarningThreshold { get; set; }

    /// <summary>危險門檻（紅線）。</summary>
    public double DangerThreshold { get; set; }

    /// <summary>
    /// 選填；讀取時<b>忽略</b>（狀態為衍生值，由最新值對門檻判定）。
    /// 產生器會一併寫入，方便人工檢視檔案內容。
    /// </summary>
    public string? Status { get; set; }

    /// <summary>圖檔 data URI（如 <c>data:image/png;base64,...</c>）。</summary>
    public string? Image { get; set; }

    /// <summary>量測序列（含時間，通常 10 筆）。</summary>
    public List<MeasurementFileModel> Measurements { get; set; } = [];
}

/// <summary>JSON 內的單筆量測。</summary>
public sealed class MeasurementFileModel
{
    /// <summary>量測時間。</summary>
    public DateTime Time { get; set; }

    /// <summary>量測值。</summary>
    public double Value { get; set; }
}
