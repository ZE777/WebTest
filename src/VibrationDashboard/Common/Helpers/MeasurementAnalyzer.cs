using VibrationDashboard.Models;

namespace VibrationDashboard.Common.Helpers;

/// <summary>
/// 量測序列的輕量統計分析(對齊 docs/07 §5.2「分析指標」)。純函式、無狀態、好測試;
/// 「後端算、前端畫」——把統計算好用 DTO 給前端，前端只負責呈現(見 docs/07 §8)。
/// </summary>
public static class MeasurementAnalyzer
{
    /// <summary>
    /// 對一段量測序列計算彙總統計。空序列回 <c>null</c>(呼叫端據此決定不顯示統計區)。
    /// </summary>
    /// <param name="measurements">依時間遞增的量測序列。</param>
    /// <param name="warningThreshold">警告門檻，用於計算「視窗內超標次數」。</param>
    public static MeasurementStats? Analyze(IReadOnlyList<Measurement> measurements, double warningThreshold)
    {
        if (measurements is null || measurements.Count == 0) return null;

        var values = measurements.Select(m => m.Value).ToList();
        var count = values.Count;
        var average = values.Average();
        var max = values.Max();
        var min = values.Min();

        // 母體標準差(序列即母體本身，非抽樣)；單點時為 0。
        var variance = values.Sum(v => (v - average) * (v - average)) / count;
        var stdDev = Math.Sqrt(variance);

        // 超標次數:視窗內達到警告門檻(含)以上的點數。
        var exceedWarning = values.Count(v => v >= warningThreshold);

        var slopePerDay = ComputeSlopePerDay(measurements);

        return new MeasurementStats(
            Average: Round(average),
            Max: Round(max),
            Min: Round(min),
            StdDev: Round(stdDev),
            SlopePerDay: Round(slopePerDay),
            ExceedWarning: exceedWarning,
            Count: count);
    }

    /// <summary>
    /// 線性回歸斜率，換算為「每日變化量」(正=惡化、負=改善)。
    /// 以時間(天)為 x、量測值為 y 做最小平方法;單點或時間零跨度時回 0。
    /// </summary>
    private static double ComputeSlopePerDay(IReadOnlyList<Measurement> measurements)
    {
        var n = measurements.Count;
        if (n < 2) return 0;

        // x 以「距首筆的天數」表示，避免大 ticks 數值造成浮點誤差。
        var t0 = measurements[0].Time;
        var xs = measurements.Select(m => (m.Time - t0).TotalDays).ToList();
        var ys = measurements.Select(m => m.Value).ToList();

        var meanX = xs.Average();
        var meanY = ys.Average();

        double sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - meanX;
            sxx += dx * dx;
            sxy += dx * (ys[i] - meanY);
        }

        // 所有時間相同(零跨度)→ 無法定義斜率，回 0。
        return sxx <= double.Epsilon ? 0 : sxy / sxx;
    }

    /// <summary>統一保留 4 位小數，避免浮點尾數雜訊污染 API 輸出。</summary>
    private static double Round(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}

/// <summary>
/// 量測序列統計結果(明細頁讀數頭呈現用)。對應 docs/07 §4B「統計摘要」。
/// </summary>
/// <param name="Average">平均值。</param>
/// <param name="Max">最大值。</param>
/// <param name="Min">最小值。</param>
/// <param name="StdDev">母體標準差(穩定度)。</param>
/// <param name="SlopePerDay">趨勢斜率(每日變化量；正=惡化)。</param>
/// <param name="ExceedWarning">視窗內達警告門檻(含)以上的點數。</param>
/// <param name="Count">量測筆數。</param>
public sealed record MeasurementStats(
    double Average,
    double Max,
    double Min,
    double StdDev,
    double SlopePerDay,
    int ExceedWarning,
    int Count);
