using ElectricityExplorer.Core.Models;

namespace ElectricityExplorer.Core.Nem12;

internal sealed class ChannelReadingBuffer
{
    public List<Nem12Reading> Readings { get; } = [];

    public Dictionary<DateOnly, int> DayOffsets { get; } = [];
}
