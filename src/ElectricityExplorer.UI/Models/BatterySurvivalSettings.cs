using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.UI.Models;

public sealed class BatterySurvivalSettings
{
    public const double MaximumAdditionalBatteryKwh = 100;
    public const double MaximumAdditionalSolarKw = 50;

    public double AdditionalBatteryCapacityKwh { get; set; } = 8;

    public double AdditionalSolarKw { get; set; }

    public double FreeTariffStartHour { get; set; } = 11;

    public double FreeTariffEndHour { get; set; } = 14;

    public TimeOnly BatteryFullAt { get; set; } = new(14, 0);

    public double SolarYieldKwhPerKwDay { get; set; } = 4;

    public double UsableBatteryCapacityKwh =>
        AdditionalBatteryCapacityKwh
        * BatterySurvivalOptions.BatteryUtilizationPercent
        / 100d;

    public TimeOnly FreeTariffStart => TimeOnly.FromTimeSpan(
        TimeSpan.FromHours(FreeTariffStartHour));

    public TimeOnly FreeTariffEnd => TimeOnly.FromTimeSpan(
        TimeSpan.FromHours(FreeTariffEndHour));

    public BatterySurvivalOptions ToOptions() =>
        new()
        {
            AdditionalBatteryCapacityKwh = AdditionalBatteryCapacityKwh,
            AdditionalSolarKw = AdditionalSolarKw,
            SolarYieldKwhPerKwDay = SolarYieldKwhPerKwDay,
            FreeTariffStart = FreeTariffStart,
            FreeTariffEnd = FreeTariffEnd,
            BatteryFullAt = BatteryFullAt
        };
}
