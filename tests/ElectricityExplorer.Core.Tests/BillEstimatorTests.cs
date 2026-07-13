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
    public void Calculate_AppliesOvernightRateAndFreePeriodBeforeDefaultRate()
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
            FreePeriodStart = new TimeOnly(23, 0),
            FreePeriodEnd = new TimeOnly(1, 0),
            TimeOfUseRates =
            [
                new TimeOfUseRate(
                    "Off-peak",
                    20,
                    new TimeOnly(22, 0),
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
}
