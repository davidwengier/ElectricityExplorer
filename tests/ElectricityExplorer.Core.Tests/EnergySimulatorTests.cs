using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.Core.Tests;

public sealed class EnergySimulatorTests
{
    [Fact]
    public void Run_ChargesFromExportThenDischargesIntoDemand()
    {
        var profile = new[]
        {
            new SiteInterval(new DateTime(2025, 1, 1, 12, 0, 0), 1, 0, 2),
            new SiteInterval(new DateTime(2025, 1, 1, 18, 0, 0), 1, 1.5, 0)
        };
        var options = DefaultOptions() with
        {
            BatteryCapacityKwh = 2,
            BatteryPowerKw = 5,
            InitialChargePercent = 0
        };

        var result = EnergySimulator.Run(profile, options);

        Assert.Equal(0, result.GridImportAfterBatteryKwh, precision: 6);
        Assert.Equal(0, result.GridExportAfterBatteryKwh, precision: 6);
        Assert.Equal(1.5, result.BatteryDischargeKwh, precision: 6);
        Assert.Equal(25, result.Intervals[^1].BatterySocPercent, precision: 6);
    }

    [Fact]
    public void Run_ReportsWhenTheBatteryFirstReachesItsReserve()
    {
        var firstInterval = new DateTime(2025, 1, 1, 0, 0, 0);
        var profile = new[]
        {
            new SiteInterval(firstInterval, 1, 1, 0),
            new SiteInterval(firstInterval.AddHours(1), 1, 1, 0)
        };
        var options = DefaultOptions() with
        {
            BatteryCapacityKwh = 1,
            BatteryPowerKw = 5,
            InitialChargePercent = 100
        };

        var result = EnergySimulator.Run(profile, options);

        Assert.Equal(firstInterval, result.FirstBatteryDepletedAt);
        Assert.Equal(1, result.GridImportAfterBatteryKwh, precision: 6);
    }

    [Fact]
    public void Run_AdditionalSolarMatchesTheConfiguredDailyYield()
    {
        var date = new DateTime(2025, 1, 1);
        var profile = Enumerable.Range(0, 48)
            .Select(index => new SiteInterval(date.AddMinutes(index * 30), 0.5, 0, 0))
            .ToArray();
        var options = DefaultOptions() with
        {
            BatteryCapacityKwh = 0,
            AdditionalSolarKw = 2,
            SolarYieldKwhPerKwDay = 4
        };

        var result = EnergySimulator.Run(profile, options);

        Assert.Equal(8, result.AdditionalSolarKwh, precision: 6);
        Assert.Equal(8, result.GridExportAfterBatteryKwh, precision: 6);
    }

    [Fact]
    public void Compare_SelectsTheSmallestCapacityThatCapturesMostSavings()
    {
        var profile = new[]
        {
            new SiteInterval(new DateTime(2025, 1, 1, 12, 0, 0), 1, 0, 4),
            new SiteInterval(new DateTime(2025, 1, 1, 18, 0, 0), 1, 4, 0)
        };
        var options = DefaultOptions() with
        {
            BatteryPowerKw = 10,
            InitialChargePercent = 0
        };

        var result = BatterySizer.Compare(profile, options, maximumCapacityKwh: 10);

        Assert.Equal(4, result.RecommendedCapacityKwh);
    }

    private static EnergySimulationOptions DefaultOptions() =>
        new()
        {
            RoundTripEfficiencyPercent = 100,
            ReservePercent = 0,
            InitialChargePercent = 0,
            ImportTariffPerKwh = 1,
            FeedInTariffPerKwh = 0
        };
}
