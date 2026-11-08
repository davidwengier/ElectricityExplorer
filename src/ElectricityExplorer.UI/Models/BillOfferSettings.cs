using System.Text.Json.Serialization;
using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.UI.Models;

public sealed class BillOfferSettings
{
    [JsonIgnore]
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "Offer";

    public decimal DailySupplyChargeCents { get; set; } = 100;

    public decimal DefaultImportRateCentsPerKwh { get; set; } = 30;

    public decimal FeedInTariffCentsPerKwh { get; set; } = 5;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<BillRateSettings> TimedRates { get; } = [];

    // Estimates belong to the current dataset, not the offer saved for next time.
    [JsonIgnore]
    public BillEstimateResult? Estimate { get; set; }

    [JsonIgnore]
    public string? Error { get; set; }

    public BillEstimateOptions ToOptions() =>
        new()
        {
            DailySupplyChargeCents = DailySupplyChargeCents,
            DefaultImportRateCentsPerKwh = DefaultImportRateCentsPerKwh,
            FeedInTariffCentsPerKwh = FeedInTariffCentsPerKwh,
            TimeOfUseRates = TimedRates.Select(rate => rate.ToRate()).ToArray()
        };

    public static BillOfferSettings Create(int offerNumber) =>
        new()
        {
            Name = $"Offer {offerNumber}"
        };
}
