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

        var months = profile
            .GroupBy(interval => new DateOnly(
                interval.Timestamp.Year,
                interval.Timestamp.Month,
                1))
            .OrderBy(group => group.Key)
            .Select(group => CalculateMonth(group.Key, group, options))
            .ToArray();

        return new BillEstimateResult
        {
            Months = months
        };
    }

    private static MonthlyBillEstimate CalculateMonth(
        DateOnly month,
        IEnumerable<SiteInterval> intervals,
        BillEstimateOptions options)
    {
        var coveredDays = new HashSet<DateOnly>();
        var importedKwh = 0d;
        var freeImportKwh = 0d;
        var exportedKwh = 0d;
        var usageChargeDollars = 0m;

        foreach (var interval in intervals)
        {
            coveredDays.Add(DateOnly.FromDateTime(interval.Timestamp));
            importedKwh += interval.ImportKwh;
            exportedKwh += interval.ExportKwh;

            var time = TimeOnly.FromDateTime(interval.Timestamp);
            if (IsFree(time, options))
            {
                freeImportKwh += interval.ImportKwh;
                continue;
            }

            var rate = options.TimeOfUseRates
                .FirstOrDefault(candidate => IsWithinPeriod(time, candidate.Start, candidate.End))
                ?.CentsPerKwh
                ?? options.DefaultImportRateCentsPerKwh;
            usageChargeDollars +=
                (decimal)interval.ImportKwh * rate / 100m;
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
            usageChargeDollars,
            supplyChargeDollars,
            feedInCreditDollars);
    }

    private static bool IsFree(TimeOnly time, BillEstimateOptions options) =>
        options.FreePeriodStart is { } start
        && options.FreePeriodEnd is { } end
        && IsWithinPeriod(time, start, end);

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
