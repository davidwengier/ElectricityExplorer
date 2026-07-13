namespace ElectricityExplorer.Core.Analysis;

public sealed record BatterySurvivalOptions
{
    public const double BatteryUtilizationPercent = 80;

    public double AdditionalBatteryCapacityKwh { get; init; } = 8;

    public double AdditionalSolarKw { get; init; }

    public double SolarYieldKwhPerKwDay { get; init; } = 4;

    public TimeOnly FreeTariffStart { get; init; } = new(11, 0);

    public TimeOnly FreeTariffEnd { get; init; } = new(14, 0);

    public TimeOnly BatteryFullAt { get; init; } = new(14, 0);

    public double UsableBatteryCapacityKwh =>
        AdditionalBatteryCapacityKwh * BatteryUtilizationPercent / 100d;

    public void Validate()
    {
        EnsureRange(AdditionalBatteryCapacityKwh, 0, 200, nameof(AdditionalBatteryCapacityKwh));
        EnsureRange(AdditionalSolarKw, 0, 200, nameof(AdditionalSolarKw));
        EnsureRange(SolarYieldKwhPerKwDay, 0, 20, nameof(SolarYieldKwhPerKwDay));

        if (FreeTariffStart == FreeTariffEnd)
        {
            throw new ArgumentException("The free tariff period must have a start and an end.");
        }
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
