namespace ElectricityExplorer.Core.Analysis;

public sealed record BatterySizingPoint(
    double CapacityKwh,
    double GridImportKwh,
    double BatterySavings);
