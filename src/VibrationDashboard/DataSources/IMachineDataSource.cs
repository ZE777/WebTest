using VibrationDashboard.Models;

namespace VibrationDashboard.DataSources;

/// <summary>
/// 設備資料存取抽象（策略模式 + 依賴反轉）。把「資料從哪來」與業務/呈現解耦，
/// 讓 JSON / SQL 來源可由設定切換而不影響 Service / Controller（見 docs/02 §3）。
/// </summary>
public interface IMachineDataSource
{
    /// <summary>取得所有設備（含量測序列）。</summary>
    Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken ct = default);

    /// <summary>依識別碼取得單一設備；不存在時回傳 <c>null</c>。</summary>
    Task<Machine?> GetByIdAsync(string id, CancellationToken ct = default);
}
