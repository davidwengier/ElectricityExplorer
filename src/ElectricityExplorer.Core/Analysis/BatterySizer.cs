namespace ElectricityExplorer.Core.Analysis;

public static class BatterySizer
{
    public static BatterySizingResult Compare(
        IReadOnlyList<SiteInterval> profile,
        EnergySimulationOptions options,
        double maximumCapacityKwh,
        double stepKwh = 1)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        if (!double.IsFinite(maximumCapacityKwh)
            || maximumCapacityKwh < 1
            || maximumCapacityKwh > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCapacityKwh),
                "The maximum comparison capacity must be between 1 and 200 kWh.");
        }

        if (!double.IsFinite(stepKwh) || stepKwh <= 0 || stepKwh > maximumCapacityKwh)
        {
            throw new ArgumentOutOfRangeException(nameof(stepKwh));
        }

        var points = new List<BatterySizingPoint>();
        for (var capacity = 0d; capacity <= maximumCapacityKwh + stepKwh / 2d; capacity += stepKwh)
        {
            var testedCapacity = Math.Min(capacity, maximumCapacityKwh);
            var result = EnergySimulator.Run(
                profile,
                options with
                {
                    BatteryCapacityKwh = testedCapacity,
                    InitialChargePercent = options.ReservePercent
                },
                includeIntervals: false);

            points.Add(new BatterySizingPoint(
                testedCapacity,
                result.GridImportAfterBatteryKwh,
                result.BatterySavings));

            if (testedCapacity >= maximumCapacityKwh)
            {
                break;
            }
        }

        var maximumSavings = points.Max(point => point.BatterySavings);
        if (maximumSavings <= 0.01)
        {
            return new BatterySizingResult
            {
                Points = points,
                MaximumTestedCapacityKwh = maximumCapacityKwh
            };
        }

        var targetSavings = maximumSavings * 0.9;
        var recommendation = points.First(point => point.BatterySavings >= targetSavings);

        return new BatterySizingResult
        {
            Points = points,
            RecommendedCapacityKwh = recommendation.CapacityKwh,
            MaximumTestedCapacityKwh = maximumCapacityKwh,
            SavingsAtRecommendedCapacity = recommendation.BatterySavings
        };
    }
}
