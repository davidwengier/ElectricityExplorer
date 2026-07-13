using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.Core.Tests;

public sealed class BatterySurvivalAnalyzerTests
{
    [Fact]
    public void Run_ReportsDepletionAndCapsDaysThatReachTheFreePeriod()
    {
        var firstStart = new DateTime(2025, 1, 1, 14, 0, 0);
        var profile = HourlyProfile(
            firstStart,
            new DateTime(2025, 1, 3, 11, 0, 0),
            timestamp => timestamp switch
            {
                { Day: 1, Hour: 14 or 15 } => 1,
                _ => 0
            });
        var options = DefaultOptions() with
        {
            AdditionalBatteryCapacityKwh = 1.875,
            BatteryFullAt = new TimeOnly(14, 0)
        };

        var result = BatterySurvivalAnalyzer.Run(profile, options);

        Assert.Equal(2, result.Days.Count);
        Assert.Equal(new DateTime(2025, 1, 1, 15, 30, 0), result.Days[0].DepletedAt);
        Assert.Equal(1.5, result.Days[0].RuntimeHours, precision: 6);
        Assert.True(result.Days[1].ReachedTarget);
        Assert.Equal(21, result.Days[1].RuntimeHours, precision: 6);
        Assert.Equal(50, result.ReachedTargetPercent, precision: 6);
    }

    [Fact]
    public void Run_AdditionalSolarCanCarryTheBatteryToTheFreePeriod()
    {
        var start = new DateTime(2025, 1, 1);
        var profile = HourlyProfile(
            start,
            new DateTime(2025, 1, 1, 11, 0, 0),
            timestamp => timestamp.Hour == 9 ? 0.5 : 0);
        var withoutSolar = DefaultOptions() with
        {
            AdditionalBatteryCapacityKwh = 0.3125
        };
        var withSolar = withoutSolar with
        {
            AdditionalSolarKw = 10
        };

        var depleted = BatterySurvivalAnalyzer.Run(profile, withoutSolar);
        var survived = BatterySurvivalAnalyzer.Run(profile, withSolar);

        Assert.Equal(new DateTime(2025, 1, 1, 9, 30, 0), depleted.Days[0].DepletedAt);
        Assert.True(survived.Days[0].ReachedTarget);
    }

    [Fact]
    public void Run_DoesNotUseTheBatteryDuringTheFreeTariffPeriod()
    {
        var start = new DateTime(2025, 1, 1, 12, 0, 0);
        var profile = HourlyProfile(
            start,
            new DateTime(2025, 1, 2, 11, 0, 0),
            timestamp => timestamp.Hour is 12 or 13 or 14 ? 1 : 0);
        var options = DefaultOptions() with
        {
            AdditionalBatteryCapacityKwh = 1.25,
            BatteryFullAt = new TimeOnly(12, 0)
        };

        var result = BatterySurvivalAnalyzer.Run(profile, options);

        Assert.Equal(new DateTime(2025, 1, 1, 15, 0, 0), result.Days[0].DepletedAt);
    }

    [Fact]
    public void Run_SkipsAWindowWithMissingIntervals()
    {
        var start = new DateTime(2025, 1, 1);
        var profile = HourlyProfile(
                start,
                new DateTime(2025, 1, 1, 11, 0, 0),
                _ => 0)
            .Where(interval => interval.Timestamp.Hour != 5)
            .ToArray();

        var result = BatterySurvivalAnalyzer.Run(profile, DefaultOptions());

        Assert.Empty(result.Days);
    }

    [Fact]
    public void Run_UsesEightyPercentOfTheNominalBatteryCapacity()
    {
        var start = new DateTime(2025, 1, 1);
        var profile = HourlyProfile(
            start,
            new DateTime(2025, 1, 1, 11, 0, 0),
            timestamp => timestamp.Hour == 0 ? 1 : 0);
        var options = DefaultOptions() with
        {
            AdditionalBatteryCapacityKwh = 1
        };

        var result = BatterySurvivalAnalyzer.Run(profile, options);

        Assert.Equal(new DateTime(2025, 1, 1, 0, 48, 0), result.Days[0].DepletedAt);
    }

    private static BatterySurvivalOptions DefaultOptions() =>
        new()
        {
            FreeTariffStart = new TimeOnly(11, 0),
            FreeTariffEnd = new TimeOnly(14, 0),
            BatteryFullAt = TimeOnly.MinValue,
            SolarYieldKwhPerKwDay = 4
        };

    private static SiteInterval[] HourlyProfile(
        DateTime start,
        DateTime end,
        Func<DateTime, double> importForTimestamp)
    {
        var intervals = new List<SiteInterval>();
        for (var timestamp = start; timestamp < end; timestamp = timestamp.AddHours(1))
        {
            intervals.Add(new SiteInterval(
                timestamp,
                1,
                importForTimestamp(timestamp),
                0));
        }

        return intervals.ToArray();
    }
}
