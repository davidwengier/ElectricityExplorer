namespace ElectricityExplorer.Core.Analysis;

public sealed record MonthlyBillEstimate(
    DateOnly Month,
    int CoveredDays,
    double ImportedKwh,
    double FreeImportKwh,
    double ExportedKwh,
    decimal UsageChargeDollars,
    decimal SupplyChargeDollars,
    decimal FeedInCreditDollars)
{
    public decimal TotalCostDollars =>
        UsageChargeDollars + SupplyChargeDollars - FeedInCreditDollars;
}
