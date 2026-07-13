namespace ElectricityExplorer.Core.Analysis;

public sealed record BillEstimateOptions
{
    public decimal DailySupplyChargeCents { get; init; } = 100;

    public decimal DefaultImportRateCentsPerKwh { get; init; } = 30;

    public decimal FeedInTariffCentsPerKwh { get; init; } = 5;

    public TimeOnly? FreePeriodStart { get; init; }

    public TimeOnly? FreePeriodEnd { get; init; }

    public IReadOnlyList<TimeOfUseRate> TimeOfUseRates { get; init; } = [];

    public void Validate()
    {
        EnsureRate(DailySupplyChargeCents, nameof(DailySupplyChargeCents));
        EnsureRate(DefaultImportRateCentsPerKwh, nameof(DefaultImportRateCentsPerKwh));
        EnsureRate(FeedInTariffCentsPerKwh, nameof(FeedInTariffCentsPerKwh));

        if (FreePeriodStart.HasValue != FreePeriodEnd.HasValue)
        {
            throw new ArgumentException("The free period must have both a start and an end.");
        }

        if (FreePeriodStart == FreePeriodEnd && FreePeriodStart.HasValue)
        {
            throw new ArgumentException("The free period start and end must be different.");
        }

        if (TimeOfUseRates.Count > 2)
        {
            throw new ArgumentException(
                "At most two timed rates can be added to the default import rate.",
                nameof(TimeOfUseRates));
        }

        for (var index = 0; index < TimeOfUseRates.Count; index++)
        {
            var rate = TimeOfUseRates[index];
            EnsureRate(rate.CentsPerKwh, $"{nameof(TimeOfUseRates)}[{index}]");

            if (rate.Start == rate.End)
            {
                throw new ArgumentException(
                    $"{rate.Name} must have different start and end times.",
                    nameof(TimeOfUseRates));
            }

            for (var otherIndex = 0; otherIndex < index; otherIndex++)
            {
                if (PeriodsOverlap(rate, TimeOfUseRates[otherIndex]))
                {
                    throw new ArgumentException(
                        $"{rate.Name} overlaps {TimeOfUseRates[otherIndex].Name}.",
                        nameof(TimeOfUseRates));
                }
            }
        }
    }

    private static void EnsureRate(decimal value, string propertyName)
    {
        if (value is < 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                value,
                $"{propertyName} must be between 0 and 100,000 cents.");
        }
    }

    private static bool PeriodsOverlap(TimeOfUseRate first, TimeOfUseRate second) =>
        GetSegments(first.Start, first.End)
            .Any(firstSegment =>
                GetSegments(second.Start, second.End)
                    .Any(secondSegment =>
                        Math.Max(firstSegment.Start, secondSegment.Start)
                        < Math.Min(firstSegment.End, secondSegment.End)));

    private static IReadOnlyList<(long Start, long End)> GetSegments(
        TimeOnly start,
        TimeOnly end)
    {
        if (start < end)
        {
            return [(start.Ticks, end.Ticks)];
        }

        return
        [
            (start.Ticks, TimeSpan.TicksPerDay),
            (0, end.Ticks)
        ];
    }
}
