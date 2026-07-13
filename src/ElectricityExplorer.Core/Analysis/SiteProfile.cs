using ElectricityExplorer.Core.Models;

namespace ElectricityExplorer.Core.Analysis;

public static class SiteProfile
{
    public static IReadOnlyList<SiteInterval> Build(ElectricityDataset dataset, string nmi)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var selectedChannels = dataset.Channels
            .Where(channel =>
                string.Equals(channel.Nmi, nmi, StringComparison.OrdinalIgnoreCase)
                && channel.Direction != EnergyFlowDirection.Ignore)
            .ToDictionary(channel => channel.Id, StringComparer.Ordinal);

        if (selectedChannels.Count == 0)
        {
            throw new InvalidOperationException(
                $"NMI {nmi} has no channels mapped as import or export.");
        }

        var intervalLengths = selectedChannels.Values
            .Select(channel => channel.IntervalMinutes)
            .Distinct()
            .ToArray();

        if (intervalLengths.Length != 1)
        {
            throw new InvalidOperationException(
                "All mapped channels for an NMI must use the same interval length.");
        }

        var durationHours = intervalLengths[0] / 60d;
        var intervals = new Dictionary<DateTime, SiteIntervalAccumulator>();

        foreach (var reading in dataset.Readings)
        {
            if (!selectedChannels.TryGetValue(reading.ChannelId, out var channel))
            {
                continue;
            }

            if (!intervals.TryGetValue(reading.Timestamp, out var interval))
            {
                interval = new SiteIntervalAccumulator();
                intervals.Add(reading.Timestamp, interval);
            }

            if (channel.Direction == EnergyFlowDirection.Import)
            {
                interval.ImportKwh += reading.EnergyKwh;
            }
            else if (channel.Direction == EnergyFlowDirection.Export)
            {
                interval.ExportKwh += reading.EnergyKwh;
            }
        }

        return intervals
            .OrderBy(pair => pair.Key)
            .Select(pair => new SiteInterval(
                pair.Key,
                durationHours,
                pair.Value.ImportKwh,
                pair.Value.ExportKwh))
            .ToArray();
    }

}
