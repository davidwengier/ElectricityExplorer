namespace ElectricityExplorer.Core.Analysis;

public static class BatterySurvivalAnalyzer
{
    private const double Epsilon = 0.0000001;

    public static BatterySurvivalResult Run(
        IReadOnlyList<SiteInterval> profile,
        BatterySurvivalOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (profile.Count == 0)
        {
            return EmptyResult(options);
        }

        ValidateProfile(profile);

        var intervalDuration = TimeSpan.FromHours(profile[0].DurationHours);
        var coverageStart = profile[0].Timestamp;
        var coverageEnd = profile[^1].Timestamp + intervalDuration;
        var results = new List<BatterySurvivalDay>();

        for (var date = coverageStart.Date; date <= coverageEnd.Date; date = date.AddDays(1))
        {
            var batteryFullAt = date + options.BatteryFullAt.ToTimeSpan();
            var targetAt = NextOccurrence(batteryFullAt, options.FreeTariffStart);

            if (batteryFullAt < coverageStart || targetAt > coverageEnd)
            {
                continue;
            }

            var firstInterval = FindInterval(profile, batteryFullAt);
            if (!HasCompleteWindow(profile, firstInterval, batteryFullAt, targetAt, intervalDuration))
            {
                continue;
            }

            results.Add(SimulateDay(
                profile,
                firstInterval,
                batteryFullAt,
                targetAt,
                intervalDuration,
                options));
        }

        return new BatterySurvivalResult
        {
            Days = results
        };
    }

    private static BatterySurvivalDay SimulateDay(
        IReadOnlyList<SiteInterval> profile,
        int firstInterval,
        DateTime batteryFullAt,
        DateTime targetAt,
        TimeSpan intervalDuration,
        BatterySurvivalOptions options)
    {
        var capacity = options.UsableBatteryCapacityKwh;
        if (capacity <= Epsilon)
        {
            return new BatterySurvivalDay(
                batteryFullAt,
                targetAt,
                batteryFullAt,
                0,
                0);
        }

        var storedEnergy = capacity;
        var intervalCount = GetIntervalCount(batteryFullAt, targetAt, intervalDuration);

        for (var offset = 0; offset < intervalCount; offset++)
        {
            var interval = profile[firstInterval + offset];

            if (IsWithinTariffPeriod(
                    TimeOnly.FromDateTime(interval.Timestamp),
                    options.FreeTariffStart,
                    options.FreeTariffEnd))
            {
                continue;
            }

            var additionalSolar = SolarGenerationEstimator.Estimate(
                interval,
                options.AdditionalSolarKw,
                options.SolarYieldKwhPerKwDay);
            var netDemand = interval.ImportKwh - interval.ExportKwh - additionalSolar;

            if (netDemand < -Epsilon)
            {
                storedEnergy = Math.Min(capacity, storedEnergy - netDemand);
                continue;
            }

            if (netDemand <= Epsilon)
            {
                continue;
            }

            if (netDemand >= storedEnergy - Epsilon)
            {
                var intervalFraction = Math.Clamp(storedEnergy / netDemand, 0, 1);
                var depletedAt = interval.Timestamp
                    + TimeSpan.FromTicks((long)(intervalDuration.Ticks * intervalFraction));

                if (depletedAt >= targetAt)
                {
                    return new BatterySurvivalDay(
                        batteryFullAt,
                        targetAt,
                        null,
                        (targetAt - batteryFullAt).TotalHours,
                        0);
                }

                return new BatterySurvivalDay(
                    batteryFullAt,
                    targetAt,
                    depletedAt,
                    (depletedAt - batteryFullAt).TotalHours,
                    0);
            }

            storedEnergy -= netDemand;
        }

        return new BatterySurvivalDay(
            batteryFullAt,
            targetAt,
            null,
            (targetAt - batteryFullAt).TotalHours,
            storedEnergy);
    }

    private static bool HasCompleteWindow(
        IReadOnlyList<SiteInterval> profile,
        int firstInterval,
        DateTime start,
        DateTime end,
        TimeSpan intervalDuration)
    {
        if (firstInterval < 0)
        {
            return false;
        }

        var intervalCount = GetIntervalCount(start, end, intervalDuration);
        if (intervalCount <= 0 || firstInterval + intervalCount > profile.Count)
        {
            return false;
        }

        for (var offset = 0; offset < intervalCount; offset++)
        {
            if (profile[firstInterval + offset].Timestamp != start + intervalDuration * offset)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetIntervalCount(DateTime start, DateTime end, TimeSpan intervalDuration)
    {
        var exactCount = (end - start).Ticks / (double)intervalDuration.Ticks;
        var intervalCount = (int)Math.Round(exactCount);

        return Math.Abs(exactCount - intervalCount) <= Epsilon
            ? intervalCount
            : 0;
    }

    private static int FindInterval(IReadOnlyList<SiteInterval> profile, DateTime timestamp)
    {
        var low = 0;
        var high = profile.Count - 1;

        while (low <= high)
        {
            var midpoint = low + (high - low) / 2;
            var comparison = profile[midpoint].Timestamp.CompareTo(timestamp);

            if (comparison == 0)
            {
                return midpoint;
            }

            if (comparison < 0)
            {
                low = midpoint + 1;
            }
            else
            {
                high = midpoint - 1;
            }
        }

        return -1;
    }

    private static DateTime NextOccurrence(DateTime after, TimeOnly time)
    {
        var candidate = after.Date + time.ToTimeSpan();
        return candidate <= after
            ? candidate.AddDays(1)
            : candidate;
    }

    private static bool IsWithinTariffPeriod(TimeOnly time, TimeOnly start, TimeOnly end)
    {
        if (start < end)
        {
            return time >= start && time < end;
        }

        return time >= start || time < end;
    }

    private static void ValidateProfile(IReadOnlyList<SiteInterval> profile)
    {
        var duration = profile[0].DurationHours;
        if (!double.IsFinite(duration) || duration <= 0)
        {
            throw new ArgumentException("Profile intervals must have a positive duration.", nameof(profile));
        }

        for (var index = 0; index < profile.Count; index++)
        {
            var interval = profile[index];
            if (Math.Abs(interval.DurationHours - duration) > Epsilon)
            {
                throw new ArgumentException(
                    "Battery survival analysis requires one consistent interval duration.",
                    nameof(profile));
            }

            if (index > 0 && interval.Timestamp <= profile[index - 1].Timestamp)
            {
                throw new ArgumentException(
                    "Profile intervals must be in chronological order.",
                    nameof(profile));
            }
        }
    }

    private static BatterySurvivalResult EmptyResult(BatterySurvivalOptions options) =>
        new();
}
