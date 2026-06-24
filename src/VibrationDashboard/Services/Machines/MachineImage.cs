namespace VibrationDashboard.Services.Machines;

/// <summary>
/// 圖檔服務結果(Service → Controller 的內部傳輸,非 JSON 契約)。
/// </summary>
/// <param name="Bytes">圖檔二位元內容。</param>
/// <param name="ContentType">MIME 類型(如 image/png)。</param>
/// <param name="ETag">內容雜湊產生的實體標籤,供條件式請求(304)。</param>
public sealed record MachineImage(byte[] Bytes, string ContentType, string ETag);
