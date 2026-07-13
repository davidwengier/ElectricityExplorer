namespace ElectricityExplorer.Core.Analysis;

public sealed class EnergySimulationResult
{
    public IReadOnlyList<SimulationInterval> Intervals { get; init; } = [];

    public double OriginalImportKwh { get; init; }

    public double OriginalExportKwh { get; init; }

    public double AdditionalSolarKwh { get; init; }

    public double GridImportBeforeBatteryKwh { get; init; }

    public double GridExportBeforeBatteryKwh { get; init; }

    public double GridImportAfterBatteryKwh { get; init; }

    public double GridExportAfterBatteryKwh { get; init; }

    public double OriginalCost { get; init; }

    public double SolarOnlyCost { get; init; }

    public double FinalCost { get; init; }

    public double CombinedSavings => OriginalCost - FinalCost;

    public double BatterySavings => SolarOnlyCost - FinalCost;

    public double GridImportReductionKwh => OriginalImportKwh - GridImportAfterBatteryKwh;

    public double BatteryDischargeKwh { get; init; }

    public double EquivalentBatteryCycles { get; init; }

    public DateTime? FirstBatteryDepletedAt { get; init; }

    public int CoveredDays { get; init; }
}
