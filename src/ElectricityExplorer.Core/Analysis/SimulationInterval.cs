namespace ElectricityExplorer.Core.Analysis;

public sealed record SimulationInterval(
    DateTime Timestamp,
    double OriginalImportKwh,
    double OriginalExportKwh,
    double AdditionalSolarKwh,
    double GridImportBeforeBatteryKwh,
    double GridExportBeforeBatteryKwh,
    double GridImportAfterBatteryKwh,
    double GridExportAfterBatteryKwh,
    double BatteryChargeKwh,
    double BatterySocPercent);
