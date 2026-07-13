namespace ElectricityExplorer.Core.Analysis;

public sealed record EnergySimulationOptions
{
    public double BatteryCapacityKwh { get; init; } = 10;

    public double BatteryPowerKw { get; init; } = 5;

    public double RoundTripEfficiencyPercent { get; init; } = 90;

    public double ReservePercent { get; init; } = 10;

    public double InitialChargePercent { get; init; } = 50;

    public double AdditionalSolarKw { get; init; }

    public double SolarYieldKwhPerKwDay { get; init; } = 4;

    public double ImportTariffPerKwh { get; init; } = 0.35;

    public double FeedInTariffPerKwh { get; init; } = 0.05;

    public void Validate()
    {
        EnsureRange(BatteryCapacityKwh, 0, 200, nameof(BatteryCapacityKwh));
        EnsureRange(BatteryPowerKw, 0, 100, nameof(BatteryPowerKw));
        EnsureRange(RoundTripEfficiencyPercent, 1, 100, nameof(RoundTripEfficiencyPercent));
        EnsureRange(ReservePercent, 0, 99, nameof(ReservePercent));
        EnsureRange(InitialChargePercent, ReservePercent, 100, nameof(InitialChargePercent));
        EnsureRange(AdditionalSolarKw, 0, 200, nameof(AdditionalSolarKw));
        EnsureRange(SolarYieldKwhPerKwDay, 0, 20, nameof(SolarYieldKwhPerKwDay));
        EnsureRange(ImportTariffPerKwh, 0, 10, nameof(ImportTariffPerKwh));
        EnsureRange(FeedInTariffPerKwh, 0, 10, nameof(FeedInTariffPerKwh));
    }

    private static void EnsureRange(double value, double minimum, double maximum, string propertyName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                value,
                $"{propertyName} must be between {minimum} and {maximum}.");
        }
    }
}
