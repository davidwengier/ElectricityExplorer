using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.Core.Tests;

public sealed class BillEstimatorTests
{
    [Fact]
    public void Calculate_GroupsUsageSupplyAndFeedInByMonth()
    {
        var profile = new[]
        {
            new SiteInterval(new DateTime(2025, 1, 31, 12, 0, 0), 1, 2, 1),
            new SiteInterval(new DateTime(2025, 2, 1, 12, 0, 0), 1, 3, 2),
            new SiteInterval(new DateTime(2025, 2, 2, 12, 0, 0), 1, 1, 0)
        };
        var options = new BillEstimateOptions
        {
            DailySupplyChargeCents = 100,
            DefaultImportRateCentsPerKwh = 30,
            FeedInTariffCentsPerKwh = 5
        };

        var result = BillEstimator.Calculate(profile, options);

        Assert.Equal(2, result.Months.Count);
        Assert.Equal(1, result.Months[0].CoveredDays);
        Assert.Equal(1.55m, result.Months[0].TotalCostDollars);
        Assert.Equal(2, result.Months[1].CoveredDays);
        Assert.Equal(3.10m, result.Months[1].TotalCostDollars);
        Assert.Equal(4.65m, result.TotalCostDollars);
    }

    [Fact]
    public void Calculate_AppliesOvernightFreePeriodAndTimedRateBeforeDefaultRate()
    {
        var date = new DateTime(2025, 1, 1);
        var profile = new[]
        {
            new SiteInterval(date, 1, 1, 0),
            new SiteInterval(date.AddHours(6), 1, 1, 0),
            new SiteInterval(date.AddHours(12), 1, 1, 1),
            new SiteInterval(date.AddHours(18), 1, 1, 0)
        };
        var options = new BillEstimateOptions
        {
            DailySupplyChargeCents = 100,
            DefaultImportRateCentsPerKwh = 30,
            FeedInTariffCentsPerKwh = 5,
            TimeOfUseRates =
            [
                new TimeOfUseRate(
                    "Free",
                    0,
                    new TimeOnly(23, 0),
                    new TimeOnly(1, 0)),
                new TimeOfUseRate(
                    "Off-peak",
                    20,
                    new TimeOnly(1, 0),
                    new TimeOnly(7, 0))
            ]
        };

        var result = BillEstimator.Calculate(profile, options);
        var month = Assert.Single(result.Months);

        Assert.Equal(1, month.FreeImportKwh, precision: 6);
        Assert.Equal(0.80m, month.UsageChargeDollars);
        Assert.Equal(0.05m, month.FeedInCreditDollars);
        Assert.Equal(1.75m, month.TotalCostDollars);
    }

    [Fact]
    public void Calculate_SplitsFreeImportsAtCapAndResetsForNextOccurrence()
    {
        var date = new DateTime(2025, 1, 1, 11, 0, 0);
        var profile = new[]
        {
            new SiteInterval(date, 1, 10, 0),
            new SiteInterval(date.AddHours(1), 1, 8, 0),
            new SiteInterval(date.AddHours(2), 1, 2, 0),
            new SiteInterval(date.AddDays(1), 1, 10, 0)
        };
        var options = new BillEstimateOptions
        {
            DailySupplyChargeCents = 0,
            TimeOfUseRates =
            [
                new TimeOfUseRate("Free", 0, new TimeOnly(11, 0), new TimeOnly(14, 0), 15, 30)
            ]
        };

        var month = Assert.Single(BillEstimator.Calculate(profile, options).Months);

        Assert.Equal(30, month.ImportedKwh, precision: 6);
        Assert.Equal(25, month.FreeImportKwh, precision: 6);
        Assert.Equal(1.50m, month.UsageChargeDollars);
    }

    [Fact]
    public void Calculate_SplitsPaidRateAtCap()
    {
        var profile = new[]
        {
            new SiteInterval(new DateTime(2025, 1, 1, 12, 0, 0), 1, 20, 0)
        };
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Discounted", 10, new TimeOnly(11, 0), new TimeOnly(14, 0), 15, 30)
            ]
        };

        var month = Assert.Single(BillEstimator.Calculate(profile, options).Months);

        Assert.Equal(3m, month.UsageChargeDollars);
        Assert.Equal(0, month.FreeImportKwh);
    }

    [Fact]
    public void Calculate_KeepsOvernightCapAcrossMonthBoundaryAndOrdersReadings()
    {
        var date = new DateTime(2025, 1, 31, 22, 0, 0);
        // Deliberately put the later January reading first: the allowance goes to earlier usage.
        var profile = new[]
        {
            new SiteInterval(date.AddHours(1), 1, 8, 0),
            new SiteInterval(date, 1, 10, 0),
            new SiteInterval(date.AddHours(3), 1, 2, 1),
            new SiteInterval(date.AddDays(1), 1, 10, 0)
        };
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Overnight", 0, new TimeOnly(22, 0), new TimeOnly(7, 0), 15, 30)
            ]
        };

        var result = BillEstimator.Calculate(profile, options);

        Assert.Equal(2, result.Months.Count);
        Assert.Equal(15, result.Months[0].FreeImportKwh, precision: 6);
        Assert.Equal(0.90m, result.Months[0].UsageChargeDollars);
        Assert.Equal(10, result.Months[1].FreeImportKwh, precision: 6);
        Assert.Equal(0.60m, result.Months[1].UsageChargeDollars);
        Assert.Equal(0.05m, result.Months[1].FeedInCreditDollars);
        Assert.Equal(1, result.Months[0].CoveredDays);
        Assert.Equal(1, result.Months[1].CoveredDays);
    }

    [Fact]
    public void Calculate_TracksCapsSeparatelyAndAcceptsMoreThanTwoAdjacentPeriods()
    {
        var date = new DateTime(2025, 1, 1);
        var profile = new[]
        {
            new SiteInterval(date.AddHours(11), 1, 5, 0),
            new SiteInterval(date.AddHours(12), 1, 5, 0),
            new SiteInterval(date.AddHours(13), 1, 5, 0),
            new SiteInterval(date.AddHours(14), 1, 5, 0)
        };
        var options = new BillEstimateOptions
        {
            DefaultImportRateCentsPerKwh = 40,
            TimeOfUseRates =
            [
                new TimeOfUseRate("Morning", 0, new TimeOnly(11, 0), new TimeOnly(12, 0), 3, 10),
                new TimeOfUseRate("Midday", 0, new TimeOnly(12, 0), new TimeOnly(13, 0), 3, 20),
                new TimeOfUseRate("Afternoon", 30, new TimeOnly(13, 0), new TimeOnly(14, 0))
            ]
        };

        var month = Assert.Single(BillEstimator.Calculate(profile, options).Months);

        Assert.Equal(6, month.FreeImportKwh, precision: 6);
        Assert.Equal(4.10m, month.UsageChargeDollars);
    }

    [Fact]
    public void Calculate_AllowsZeroCapAndFreeAboveCapRate()
    {
        var profile = new[]
        {
            new SiteInterval(new DateTime(2025, 1, 1, 12, 0, 0), 1, 5, 0)
        };
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Free above cap", 30, new TimeOnly(11, 0), new TimeOnly(14, 0), 0, 0)
            ]
        };

        var month = Assert.Single(BillEstimator.Calculate(profile, options).Months);

        Assert.Equal(5, month.FreeImportKwh, precision: 6);
        Assert.Equal(0m, month.UsageChargeDollars);
    }

    [Fact]
    public void Calculate_RejectsOverlappingTimedRates()
    {
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate(
                    "Peak",
                    50,
                    new TimeOnly(15, 0),
                    new TimeOnly(21, 0)),
                new TimeOfUseRate(
                    "Shoulder",
                    40,
                    new TimeOnly(20, 0),
                    new TimeOnly(22, 0))
            ]
        };

        var exception = Assert.Throws<ArgumentException>(
            () => BillEstimator.Calculate([], options));

        Assert.Contains("overlaps", exception.Message);
    }

    [Fact]
    public void Calculate_RejectsOverlapInMorningPartOfOvernightPeriod()
    {
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Overnight", 0, new TimeOnly(22, 0), new TimeOnly(7, 0)),
                new TimeOfUseRate("Morning", 20, new TimeOnly(6, 0), new TimeOnly(8, 0))
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => BillEstimator.Calculate([], options));

        Assert.Contains("overlaps", exception.Message);
    }

    [Fact]
    public void Calculate_RejectsCapWithoutSecondaryRate()
    {
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Incomplete", 0, new TimeOnly(11, 0), new TimeOnly(14, 0), 15)
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => BillEstimator.Calculate([], options));

        Assert.Contains("both a usage cap and an above-cap rate", exception.Message);
    }

    [Fact]
    public void Calculate_RejectsNonFiniteCap()
    {
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Invalid", 0, new TimeOnly(11, 0), new TimeOnly(14, 0), double.NaN, 30)
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => BillEstimator.Calculate([], options));
    }

    [Fact]
    public void Calculate_RejectsNegativeCap()
    {
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Invalid", 0, new TimeOnly(11, 0), new TimeOnly(14, 0), -1, 30)
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => BillEstimator.Calculate([], options));
    }

    [Fact]
    public void Calculate_RejectsNegativeSecondaryRate()
    {
        var options = new BillEstimateOptions
        {
            TimeOfUseRates =
            [
                new TimeOfUseRate("Invalid", 0, new TimeOnly(11, 0), new TimeOnly(14, 0), 15, -1)
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => BillEstimator.Calculate([], options));
    }
}
