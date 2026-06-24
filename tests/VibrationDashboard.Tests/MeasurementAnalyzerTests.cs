using VibrationDashboard.Common.Helpers;
using VibrationDashboard.Models;

namespace VibrationDashboard.Tests;

/// <summary>
/// 驗證量測統計分析(平均/標準差/超標次數/趨勢斜率)與邊界(單點、空、零跨度)。
/// </summary>
public class MeasurementAnalyzerTests
{
    private static List<Measurement> Series(params (int dayOffset, double value)[] points)
    {
        var t0 = new DateTime(2026, 6, 1, 8, 0, 0);
        return points.Select(p => new Measurement(t0.AddDays(p.dayOffset), p.value)).ToList();
    }

    [Fact]
    public void Analyze_EmptySeries_ReturnsNull()
    {
        Assert.Null(MeasurementAnalyzer.Analyze([], warningThreshold: 0.5));
    }

    [Fact]
    public void Analyze_BasicStats_AreCorrect()
    {
        var s = MeasurementAnalyzer.Analyze(Series((0, 1), (1, 2), (2, 3)), warningThreshold: 2.5);

        Assert.NotNull(s);
        Assert.Equal(3, s!.Count);
        Assert.Equal(2.0, s.Average);
        Assert.Equal(3.0, s.Max);
        Assert.Equal(1.0, s.Min);
        // 母體標準差 sqrt(((1-2)^2+(2-2)^2+(3-2)^2)/3) = sqrt(2/3) ≈ 0.8165
        Assert.Equal(0.8165, s.StdDev, precision: 3);
        Assert.Equal(1, s.ExceedWarning);  // 只有 3 >= 2.5
    }

    [Fact]
    public void Analyze_MonotonicRise_HasPositiveSlopePerDay()
    {
        // 每天 +0.1：斜率應 ≈ +0.1/天(惡化)
        var s = MeasurementAnalyzer.Analyze(Series((0, 0.30), (1, 0.40), (2, 0.50), (3, 0.60)), warningThreshold: 1.0);

        Assert.NotNull(s);
        Assert.True(s!.SlopePerDay > 0, "單調上升應為正斜率");
        Assert.Equal(0.1, s.SlopePerDay, precision: 4);
    }

    [Fact]
    public void Analyze_MonotonicFall_HasNegativeSlope()
    {
        var s = MeasurementAnalyzer.Analyze(Series((0, 0.60), (1, 0.50), (2, 0.40)), warningThreshold: 1.0);

        Assert.NotNull(s);
        Assert.True(s!.SlopePerDay < 0, "單調下降應為負斜率(改善)");
    }

    [Fact]
    public void Analyze_SinglePoint_SlopeIsZeroAndStatsDegenerate()
    {
        var s = MeasurementAnalyzer.Analyze(Series((0, 0.42)), warningThreshold: 0.5);

        Assert.NotNull(s);
        Assert.Equal(1, s!.Count);
        Assert.Equal(0, s.SlopePerDay);    // 不足兩點 → 0
        Assert.Equal(0, s.StdDev);         // 單點無離散
        Assert.Equal(0.42, s.Average);
        Assert.Equal(0, s.ExceedWarning);  // 0.42 < 0.5
    }

    [Fact]
    public void Analyze_AllSameTimestamp_ZeroSpanSlopeIsZero()
    {
        // 時間零跨度(同一刻多筆)→ 斜率無定義，回 0(不應拋例外/NaN)
        var t = new DateTime(2026, 6, 1, 8, 0, 0);
        var series = new List<Measurement>
        {
            new(t, 0.3), new(t, 0.9), new(t, 0.6)
        };
        var s = MeasurementAnalyzer.Analyze(series, warningThreshold: 0.5);

        Assert.NotNull(s);
        Assert.Equal(0, s!.SlopePerDay);
        Assert.False(double.IsNaN(s.SlopePerDay));
        Assert.Equal(2, s.ExceedWarning);  // 0.9 與 0.6 >= 0.5
    }
}
