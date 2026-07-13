using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.UI.Models;

public sealed class BillRateSettings
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "Peak";

    public decimal CentsPerKwh { get; set; } = 50;

    public TimeOnly Start { get; set; } = new(15, 0);

    public TimeOnly End { get; set; } = new(21, 0);

    public TimeOfUseRate ToRate() =>
        new(
            string.IsNullOrWhiteSpace(Name) ? "Timed rate" : Name.Trim(),
            CentsPerKwh,
            Start,
            End);

    public static BillRateSettings Create(int timedRateIndex) =>
        timedRateIndex == 0
            ? new BillRateSettings()
            : new BillRateSettings
            {
                Name = "Off-peak",
                CentsPerKwh = 20,
                Start = new TimeOnly(22, 0),
                End = new TimeOnly(7, 0)
            };
}
