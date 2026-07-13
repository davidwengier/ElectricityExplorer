namespace ElectricityExplorer.Core.Analysis;

public sealed class BatterySizingResult
{
    public IReadOnlyList<BatterySizingPoint> Points { get; init; } = [];

    public double RecommendedCapacityKwh { get; init; }

    public double MaximumTestedCapacityKwh { get; init; }

    public double SavingsAtRecommendedCapacity { get; init; }
}
