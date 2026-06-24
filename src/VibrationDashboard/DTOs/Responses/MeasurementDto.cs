namespace VibrationDashboard.DTOs.Responses;

/// <summary>單筆量測對外契約(時間 + 數值)。</summary>
public sealed record MeasurementDto(DateTime Time, double Value);
