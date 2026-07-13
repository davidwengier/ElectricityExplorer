using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.UI.Models;

public sealed class BillOfferSettings
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "Offer";

    public decimal DailySupplyChargeCents { get; set; } = 100;

    public decimal DefaultImportRateCentsPerKwh { get; set; } = 30;

    public decimal FeedInTariffCentsPerKwh { get; set; } = 5;

    public bool HasFreePeriod { get; set; }

    public TimeOnly FreePeriodStart { get; set; } = new(11, 0);

    public TimeOnly FreePeriodEnd { get; set; } = new(14, 0);

    public List<BillRateSettings> TimedRates { get; } = [];

    public BillEstimateResult? Estimate { get; set; }

    public string? Error { get; set; }

    public BillEstimateOptions ToOptions() =>
        new()
        {
            DailySupplyChargeCents = DailySupplyChargeCents,
            DefaultImportRateCentsPerKwh = DefaultImportRateCentsPerKwh,
            FeedInTariffCentsPerKwh = FeedInTariffCentsPerKwh,
            FreePeriodStart = HasFreePeriod ? FreePeriodStart : null,
            FreePeriodEnd = HasFreePeriod ? FreePeriodEnd : null,
            TimeOfUseRates = TimedRates.Select(rate => rate.ToRate()).ToArray()
        };

    public static BillOfferSettings Create(int offerNumber) =>
        new()
        {
            Name = $"Offer {offerNumber}"
        };
}
