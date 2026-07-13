namespace ElectricityExplorer.Core.Models;

public sealed class Nem12Reading
{
    public string ChannelId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public double EnergyKwh { get; set; }
}
