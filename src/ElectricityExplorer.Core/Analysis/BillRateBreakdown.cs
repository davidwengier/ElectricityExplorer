namespace ElectricityExplorer.Core.Analysis;

public sealed record BillRateBreakdown(
    string Name,
    decimal CentsPerKwh,
    double ImportedKwh,
    decimal UsageChargeDollars,
    bool IsAboveLimit = false);
