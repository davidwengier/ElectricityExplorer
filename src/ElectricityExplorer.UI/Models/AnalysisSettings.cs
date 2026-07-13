using System.ComponentModel.DataAnnotations;
using ElectricityExplorer.Core.Analysis;

namespace ElectricityExplorer.UI.Models;

public sealed class AnalysisSettings : IValidatableObject
{
    [Range(0, 200)]
    public double BatteryCapacityKwh { get; set; } = 10;

    [Range(0, 100)]
    public double BatteryPowerKw { get; set; } = 5;

    [Range(50, 100)]
    public double RoundTripEfficiencyPercent { get; set; } = 90;

    [Range(0, 50)]
    public double ReservePercent { get; set; } = 10;

    [Range(0, 100)]
    public double InitialChargePercent { get; set; } = 50;

    [Range(0, 200)]
    public double AdditionalSolarKw { get; set; }

    [Range(0, 20)]
    public double SolarYieldKwhPerKwDay { get; set; } = 4;

    [Range(0, 10)]
    public double ImportTariffPerKwh { get; set; } = 0.35;

    [Range(0, 10)]
    public double FeedInTariffPerKwh { get; set; } = 0.05;

    [Range(1, 100)]
    public double MaximumComparedBatteryKwh { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InitialChargePercent < ReservePercent)
        {
            yield return new ValidationResult(
                "Initial charge cannot be below the battery reserve.",
                [nameof(InitialChargePercent)]);
        }
    }

    public EnergySimulationOptions ToSimulationOptions() =>
        new()
        {
            BatteryCapacityKwh = BatteryCapacityKwh,
            BatteryPowerKw = BatteryPowerKw,
            RoundTripEfficiencyPercent = RoundTripEfficiencyPercent,
            ReservePercent = ReservePercent,
            InitialChargePercent = InitialChargePercent,
            AdditionalSolarKw = AdditionalSolarKw,
            SolarYieldKwhPerKwDay = SolarYieldKwhPerKwDay,
            ImportTariffPerKwh = ImportTariffPerKwh,
            FeedInTariffPerKwh = FeedInTariffPerKwh
        };
}
