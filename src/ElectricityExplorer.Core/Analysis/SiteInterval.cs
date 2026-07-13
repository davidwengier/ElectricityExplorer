namespace ElectricityExplorer.Core.Analysis;

public sealed record SiteInterval(
    DateTime Timestamp,
    double DurationHours,
    double ImportKwh,
    double ExportKwh);
