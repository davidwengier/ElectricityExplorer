namespace ElectricityExplorer.Core.Analysis;

public sealed record BatterySurvivalDay(
    DateTime BatteryFullAt,
    DateTime TargetAt,
    DateTime? DepletedAt,
    double RuntimeHours,
    double RemainingBatteryKwh)
{
    public bool ReachedTarget => DepletedAt is null;

    public double TargetRuntimeHours => (TargetAt - BatteryFullAt).TotalHours;
}
