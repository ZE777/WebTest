using VibrationDashboard.Common.Helpers;
using VibrationDashboard.Models;

namespace VibrationDashboard.Tests;

/// <summary>
/// 驗證狀態判定邏輯(對門檻 / ISO 分區)。門檻採「達到即進入」語意。
/// </summary>
public class StatusEvaluatorTests
{
    [Theory]
    // value, warning, danger, expected
    [InlineData(0.20, 0.50, 0.88, MachineStatus.Good)]
    [InlineData(0.49, 0.50, 0.88, MachineStatus.Good)]      // 警告線下一點點 → 良好
    [InlineData(0.50, 0.50, 0.88, MachineStatus.Abnormal)]  // 剛好達警告 → 異常(邊界)
    [InlineData(0.87, 0.50, 0.88, MachineStatus.Abnormal)]  // 危險線下一點點 → 異常
    [InlineData(0.88, 0.50, 0.88, MachineStatus.Danger)]    // 剛好達危險 → 危險(邊界)
    [InlineData(1.50, 0.50, 0.88, MachineStatus.Danger)]
    [InlineData(0.00, 0.50, 0.88, MachineStatus.Good)]
    public void Evaluate_ByValueAndThresholds_ReturnsExpectedStatus(
        double value, double warning, double danger, MachineStatus expected)
    {
        Assert.Equal(expected, StatusEvaluator.Evaluate(value, warning, danger));
    }

    [Fact]
    public void Evaluate_ByMachine_UsesLatestMeasurement()
    {
        var machine = new Machine
        {
            Id = "M1",
            Name = "測試機",
            WarningThreshold = 0.5,
            DangerThreshold = 0.88,
            Measurements = new[]
            {
                new Measurement(new DateTime(2026, 6, 22, 8, 0, 0), 0.20), // 舊值良好
                new Measurement(new DateTime(2026, 6, 23, 8, 0, 0), 0.95)  // 最新值危險
            }
        };

        Assert.Equal(MachineStatus.Danger, StatusEvaluator.Evaluate(machine));
    }

    [Fact]
    public void Evaluate_ByMachine_NoMeasurements_IsGood()
    {
        var machine = new Machine
        {
            Id = "M2",
            Name = "無資料機",
            WarningThreshold = 0.5,
            DangerThreshold = 0.88,
            Measurements = []
        };

        Assert.Equal(MachineStatus.Good, StatusEvaluator.Evaluate(machine));
    }
}
