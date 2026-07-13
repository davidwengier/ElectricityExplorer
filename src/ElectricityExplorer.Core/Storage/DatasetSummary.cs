using ElectricityExplorer.Core.Models;

namespace ElectricityExplorer.Core.Storage;

public sealed class DatasetSummary
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; }

    public int ChannelCount { get; set; }

    public int ReadingCount { get; set; }

    public List<string> Nmis { get; set; } = [];

    public DateTime? Start { get; set; }

    public DateTime? End { get; set; }

    public static DatasetSummary FromDataset(ElectricityDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        DateTime? start = dataset.Readings.Count == 0
            ? null
            : dataset.Readings.Min(reading => reading.Timestamp);
        DateTime? end = dataset.Readings.Count == 0
            ? null
            : dataset.Readings.Max(reading => reading.Timestamp);

        return new DatasetSummary
        {
            Id = dataset.Id,
            Name = dataset.Name,
            SourceFileName = dataset.SourceFileName,
            ImportedAt = dataset.ImportedAt,
            ChannelCount = dataset.Channels.Count,
            ReadingCount = dataset.Readings.Count,
            Nmis = dataset.Nmis.ToList(),
            Start = start,
            End = end
        };
    }
}
