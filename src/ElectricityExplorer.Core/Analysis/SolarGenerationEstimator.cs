using System.Collections.Concurrent;

namespace ElectricityExplorer.Core.Analysis;

internal static class SolarGenerationEstimator
{
    private const double Epsilon = 0.0000001;
    private static readonly ConcurrentDictionary<int, double> DailyWeightTotals = new();

    public static double Estimate(
        SiteInterval interval,
        double solarCapacityKw,
        double yieldKwhPerKwDay)
    {
        if (solarCapacityKw <= Epsilon || yieldKwhPerKwDay <= Epsilon)
        {
            return 0;
        }

        var intervalMinutes = (int)Math.Round(interval.DurationHours * 60d);
        var midpoint = interval.Timestamp.TimeOfDay.TotalHours + interval.DurationHours / 2d;
        var weight = Weight(midpoint);

        if (intervalMinutes <= 0 || weight <= Epsilon)
        {
            return 0;
        }

        var totalWeight = DailyWeightTotals.GetOrAdd(intervalMinutes, CalculateDailyWeight);
        var dailyGeneration = solarCapacityKw * yieldKwhPerKwDay;
        return dailyGeneration * weight / totalWeight;
    }

    private static double CalculateDailyWeight(int intervalMinutes)
    {
        var totalWeight = 0d;
        var intervalCount = 1440 / intervalMinutes;
        for (var index = 0; index < intervalCount; index++)
        {
            var hour = (index * intervalMinutes + intervalMinutes / 2d) / 60d;
            totalWeight += Weight(hour);
        }

        return totalWeight;
    }

    private static double Weight(double hour)
    {
        const double sunrise = 6;
        const double sunset = 18;

        if (hour <= sunrise || hour >= sunset)
        {
            return 0;
        }

        return Math.Sin(Math.PI * (hour - sunrise) / (sunset - sunrise));
    }
}
