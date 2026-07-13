namespace ElectricityExplorer.Core.Analysis;

public static class EnergySimulator
{
    private const double Epsilon = 0.0000001;

    public static EnergySimulationResult Run(
        IReadOnlyList<SiteInterval> profile,
        EnergySimulationOptions options,
        bool includeIntervals = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (profile.Count == 0)
        {
            return new EnergySimulationResult();
        }

        var batteryCapacity = options.BatteryCapacityKwh;
        var reserveEnergy = batteryCapacity * options.ReservePercent / 100d;
        var usableCapacity = Math.Max(0, batteryCapacity - reserveEnergy);
        var initialEnergy = batteryCapacity * options.InitialChargePercent / 100d;
        var storedEnergy = Math.Clamp(initialEnergy - reserveEnergy, 0, usableCapacity);
        var chargeEfficiency = Math.Sqrt(options.RoundTripEfficiencyPercent / 100d);
        var dischargeEfficiency = chargeEfficiency;
        var intervals = includeIntervals
            ? new List<SimulationInterval>(profile.Count)
            : null;

        var originalImport = 0d;
        var originalExport = 0d;
        var additionalSolar = 0d;
        var importBeforeBattery = 0d;
        var exportBeforeBattery = 0d;
        var importAfterBattery = 0d;
        var exportAfterBattery = 0d;
        var batteryDischarge = 0d;
        DateTime? firstBatteryDepletedAt = null;

        foreach (var interval in profile)
        {
            var estimatedSolar = SolarGenerationEstimator.Estimate(
                interval,
                options.AdditionalSolarKw,
                options.SolarYieldKwhPerKwDay);
            var netGridEnergy = interval.ImportKwh - interval.ExportKwh - estimatedSolar;
            var demand = Math.Max(0, netGridEnergy);
            var surplus = Math.Max(0, -netGridEnergy);

            var gridImport = demand;
            var gridExport = surplus;

            if (usableCapacity > Epsilon && options.BatteryPowerKw > Epsilon)
            {
                if (surplus > Epsilon)
                {
                    var acEnergyToBattery = Math.Min(surplus, options.BatteryPowerKw * interval.DurationHours);
                    var availableStorage = usableCapacity - storedEnergy;
                    acEnergyToBattery = Math.Min(acEnergyToBattery, availableStorage / chargeEfficiency);

                    storedEnergy += acEnergyToBattery * chargeEfficiency;
                    gridExport -= acEnergyToBattery;
                }
                else if (demand > Epsilon && storedEnergy > Epsilon)
                {
                    var deliverableEnergy = Math.Min(
                        storedEnergy * dischargeEfficiency,
                        options.BatteryPowerKw * interval.DurationHours);
                    var deliveredEnergy = Math.Min(demand, deliverableEnergy);

                    storedEnergy -= deliveredEnergy / dischargeEfficiency;
                    batteryDischarge += deliveredEnergy;
                    gridImport -= deliveredEnergy;

                    if (storedEnergy <= Epsilon)
                    {
                        storedEnergy = 0;
                        firstBatteryDepletedAt ??= interval.Timestamp;
                    }
                }
                else if (demand > Epsilon && storedEnergy <= Epsilon)
                {
                    firstBatteryDepletedAt ??= interval.Timestamp;
                }
            }

            originalImport += interval.ImportKwh;
            originalExport += interval.ExportKwh;
            additionalSolar += estimatedSolar;
            importBeforeBattery += demand;
            exportBeforeBattery += surplus;
            importAfterBattery += Math.Max(0, gridImport);
            exportAfterBattery += Math.Max(0, gridExport);

            var chargeKwh = batteryCapacity <= Epsilon
                ? 0
                : reserveEnergy + storedEnergy;
            var stateOfCharge = batteryCapacity <= Epsilon
                ? 0
                : chargeKwh / batteryCapacity * 100d;

            intervals?.Add(new SimulationInterval(
                    interval.Timestamp,
                    interval.ImportKwh,
                    interval.ExportKwh,
                    estimatedSolar,
                    demand,
                    surplus,
                    Math.Max(0, gridImport),
                    Math.Max(0, gridExport),
                    chargeKwh,
                    stateOfCharge));
        }

        var originalCost =
            originalImport * options.ImportTariffPerKwh
            - originalExport * options.FeedInTariffPerKwh;
        var solarOnlyCost =
            importBeforeBattery * options.ImportTariffPerKwh
            - exportBeforeBattery * options.FeedInTariffPerKwh;
        var finalCost =
            importAfterBattery * options.ImportTariffPerKwh
            - exportAfterBattery * options.FeedInTariffPerKwh;

        var coveredDays = profile
            .Select(interval => interval.Timestamp.Date)
            .Distinct()
            .Count();

        return new EnergySimulationResult
        {
            Intervals = intervals ?? [],
            OriginalImportKwh = originalImport,
            OriginalExportKwh = originalExport,
            AdditionalSolarKwh = additionalSolar,
            GridImportBeforeBatteryKwh = importBeforeBattery,
            GridExportBeforeBatteryKwh = exportBeforeBattery,
            GridImportAfterBatteryKwh = importAfterBattery,
            GridExportAfterBatteryKwh = exportAfterBattery,
            OriginalCost = originalCost,
            SolarOnlyCost = solarOnlyCost,
            FinalCost = finalCost,
            BatteryDischargeKwh = batteryDischarge,
            EquivalentBatteryCycles = batteryCapacity <= Epsilon
                ? 0
                : batteryDischarge / batteryCapacity,
            FirstBatteryDepletedAt = firstBatteryDepletedAt,
            CoveredDays = coveredDays
        };
    }

}
