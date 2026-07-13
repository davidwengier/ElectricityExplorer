namespace ElectricityExplorer.Core.Analysis;

public sealed class BatterySurvivalResult
{
    public IReadOnlyList<BatterySurvivalDay> Days { get; init; } = [];

    public int ReachedTargetDays => Days.Count(day => day.ReachedTarget);

    public double ReachedTargetPercent =>
        Days.Count == 0
            ? 0
            : ReachedTargetDays / (double)Days.Count * 100d;

    public double TargetRuntimeHours =>
        Days.Count == 0
            ? 0
            : Days[0].TargetRuntimeHours;

    public double MedianRuntimeHours
    {
        get
        {
            if (Days.Count == 0)
            {
                return 0;
            }

            var ordered = Days
                .Select(day => day.RuntimeHours)
                .Order()
                .ToArray();
            var midpoint = ordered.Length / 2;

            return ordered.Length % 2 == 0
                ? (ordered[midpoint - 1] + ordered[midpoint]) / 2d
                : ordered[midpoint];
        }
    }

    public BatterySurvivalDay? WorstDay =>
        Days.MinBy(day => day.RuntimeHours);
}
