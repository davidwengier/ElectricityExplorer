using System.Text.Json.Serialization;

namespace ElectricityExplorer.Core.Models;

public sealed class ElectricityDataset
{
    public int SchemaVersion { get; set; } = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Nem12Channel> Channels { get; set; } = [];

    public List<Nem12Reading> Readings { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> Nmis =>
        Channels.Select(channel => channel.Nmi)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
