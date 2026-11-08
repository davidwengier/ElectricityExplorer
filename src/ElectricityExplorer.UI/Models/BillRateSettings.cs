using System.Text.Json.Serialization;
using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.UI.Models;

public sealed class BillRateSettings
{
    [JsonIgnore]
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "Peak";

    public decimal CentsPerKwh { get; set; } = 50;

    public TimeOnly Start { get; set; } = new(15, 0);

    public TimeOnly End { get; set; } = new(21, 0);

    public bool HasUsageCap { get; set; }

    public double MaximumKwh { get; set; } = 15;

    public decimal AboveLimitCentsPerKwh { get; set; } = 30;

    public TimeOfUseRate ToRate() =>
        new(
            string.IsNullOrWhiteSpace(Name) ? "Period" : Name.Trim(),
            CentsPerKwh,
            Start,
            End,
            HasUsageCap ? MaximumKwh : null,
            HasUsageCap ? AboveLimitCentsPerKwh : null);

    public static BillRateSettings Create(int timedRateIndex) =>
        new()
        {
            Name = $"Period {timedRateIndex + 1}",
            CentsPerKwh = 0,
            Start = new TimeOnly(11, 0),
            End = new TimeOnly(14, 0)
        };
}
