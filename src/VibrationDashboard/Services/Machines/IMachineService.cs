using VibrationDashboard.DTOs.Responses;

namespace VibrationDashboard.Services.Machines;

/// <summary>
/// 設備業務服務:組裝對外 DTO、判定狀態、排序、產生圖檔結果。
/// 只依賴 <c>IMachineDataSource</c>,不碰具體資料來源(JSON/SQL 可換)。
/// </summary>
public interface IMachineService
{
    /// <summary>取得所有設備摘要(危險優先排序,不含大圖)。</summary>
    Task<IReadOnlyList<MachineSummaryDto>> GetSummariesAsync(CancellationToken ct = default);

    /// <summary>取得單一設備摘要(供即時推播組 delta 重用);不存在或讀取失敗時回 <c>null</c>。</summary>
    Task<MachineSummaryDto?> GetSummaryAsync(string id, CancellationToken ct = default);

    /// <summary>取得單一設備明細(含量測序列);不存在時拋 <c>NotFoundException</c>。</summary>
    Task<MachineDetailDto> GetDetailAsync(string id, CancellationToken ct = default);

    /// <summary>取得設備圖檔(含 ETag);設備不存在或無圖時回 <c>null</c>。</summary>
    Task<MachineImage?> GetImageAsync(string id, CancellationToken ct = default);
}
