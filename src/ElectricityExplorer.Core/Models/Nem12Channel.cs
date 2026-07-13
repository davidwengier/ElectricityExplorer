namespace ElectricityExplorer.Core.Models;

public sealed class Nem12Channel
{
    public string Id { get; set; } = string.Empty;

    public string Nmi { get; set; } = string.Empty;

    public string NmiConfiguration { get; set; } = string.Empty;

    public string? RegisterId { get; set; }

    public string NmiSuffix { get; set; } = string.Empty;

    public string? DataStreamIdentifier { get; set; }

    public string? MeterSerialNumber { get; set; }

    public string Unit { get; set; } = string.Empty;

    public int IntervalMinutes { get; set; }

    public EnergyFlowDirection Direction { get; set; }
}
