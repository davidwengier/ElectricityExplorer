namespace ElectricityExplorer.Core.Analysis;

public static class BillEstimator
{
    public static BillEstimateResult Calculate(
        IReadOnlyList<SiteInterval> profile,
        BillEstimateOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ValidateProfile(profile);

        // Keep cap usage across month boundaries, so an overnight window doesn't get a second allowance.
        var periodUsage = new Dictionary<(TimeOfUseRate Rate, DateOnly StartDate), decimal>();
        var months = profile
            .GroupBy(interval => new DateOnly(
                interval.Timestamp.Year,
                interval.Timestamp.Month,
                1))
            .OrderBy(group => group.Key)
            .Select(group => CalculateMonth(group.Key, group, options, periodUsage))
            .ToArray();

        return new BillEstimateResult
        {
            Months = months
        };
    }

    private static MonthlyBillEstimate CalculateMonth(
        DateOnly month,
        IEnumerable<SiteInterval> intervals,
        BillEstimateOptions options,
        Dictionary<(TimeOfUseRate Rate, DateOnly StartDate), decimal> periodUsage)
    {
        var coveredDays = new HashSet<DateOnly>();
        var importedKwh = 0d;
        var freeImportKwh = 0d;
        var exportedKwh = 0d;

        // Keep every configured rate in input order, even when a month has no usage at that rate.
        var rateBreakdown = new List<BillRateBreakdown>
        {
            new("Default rate", options.DefaultImportRateCentsPerKwh, 0, 0)
        };
        var rateIndexes = new Dictionary<TimeOfUseRate, int>();
        foreach (var rate in options.TimeOfUseRates)
        {
            rateIndexes[rate] = rateBreakdown.Count;
            rateBreakdown.Add(new BillRateBreakdown(rate.Name, rate.CentsPerKwh, 0, 0));
            if (rate.AboveLimitCentsPerKwh is { } aboveLimitRate)
            {
                rateBreakdown.Add(new BillRateBreakdown(rate.Name, aboveLimitRate, 0, 0, IsAboveLimit: true));
            }
        }

        var rateImportKwh = new double[rateBreakdown.Count];
        var rateChargesDollars = new decimal[rateBreakdown.Count];

        // Price earlier readings first, even if the caller hasn't supplied them in time order.
        foreach (var interval in intervals.OrderBy(interval => interval.Timestamp))
        {
            coveredDays.Add(DateOnly.FromDateTime(interval.Timestamp));
            importedKwh += interval.ImportKwh;
            exportedKwh += interval.ExportKwh;

            var time = TimeOnly.FromDateTime(interval.Timestamp);
            var rate = options.TimeOfUseRates
                .FirstOrDefault(candidate => IsWithinPeriod(time, candidate.Start, candidate.End));
            var importKwh = (decimal)interval.ImportKwh;
            var primaryKwh = importKwh;
            var primaryRate = rate?.CentsPerKwh ?? options.DefaultImportRateCentsPerKwh;
            var aboveLimitRate = rate?.AboveLimitCentsPerKwh ?? primaryRate;

            if (rate?.MaximumKwh is { } maximumKwh)
            {
                // The morning part of an overnight window belongs to the previous day's allowance.
                var startDate = DateOnly.FromDateTime(interval.Timestamp);
                if (rate.Start > rate.End && time < rate.End)
                {
                    startDate = startDate.AddDays(-1);
                }

                var key = (rate, startDate);
                var usedKwh = periodUsage.GetValueOrDefault(key);
                primaryKwh = Math.Min(importKwh, Math.Max(0m, (decimal)maximumKwh - usedKwh));
                periodUsage[key] = usedKwh + importKwh;
            }

            // Only the portion above the cap uses the secondary rate, not the entire crossing interval.
            var aboveLimitKwh = importKwh - primaryKwh;
            var primaryIndex = rate is null ? 0 : rateIndexes[rate];
            rateImportKwh[primaryIndex] += (double)primaryKwh;
            rateChargesDollars[primaryIndex] += primaryKwh * primaryRate / 100m;
            if (rate?.MaximumKwh is not null)
            {
                rateImportKwh[primaryIndex + 1] += (double)aboveLimitKwh;
                rateChargesDollars[primaryIndex + 1] += aboveLimitKwh * aboveLimitRate / 100m;
            }

            if (primaryRate == 0)
            {
                freeImportKwh += (double)primaryKwh;
            }

            if (aboveLimitRate == 0)
            {
                freeImportKwh += (double)aboveLimitKwh;
            }
        }

        for (var index = 0; index < rateBreakdown.Count; index++)
        {
            rateBreakdown[index] = rateBreakdown[index] with
            {
                ImportedKwh = rateImportKwh[index],
                UsageChargeDollars = rateChargesDollars[index]
            };
        }

        var supplyChargeDollars =
            coveredDays.Count * options.DailySupplyChargeCents / 100m;
        var feedInCreditDollars =
            (decimal)exportedKwh * options.FeedInTariffCentsPerKwh / 100m;

        return new MonthlyBillEstimate(
            month,
            coveredDays.Count,
            importedKwh,
            freeImportKwh,
            exportedKwh,
            rateChargesDollars.Sum(),
            supplyChargeDollars,
            feedInCreditDollars)
        {
            RateBreakdown = rateBreakdown
        };
    }

    private static bool IsWithinPeriod(TimeOnly time, TimeOnly start, TimeOnly end)
    {
        if (start < end)
        {
            return time >= start && time < end;
        }

        return time >= start || time < end;
    }

    private static void ValidateProfile(IReadOnlyList<SiteInterval> profile)
    {
        foreach (var interval in profile)
        {
            if (!double.IsFinite(interval.ImportKwh)
                || !double.IsFinite(interval.ExportKwh)
                || interval.ImportKwh < 0
                || interval.ExportKwh < 0)
            {
                throw new ArgumentException(
                    "Profile import and export values must be finite and non-negative.",
                    nameof(profile));
            }
        }
    }
}
