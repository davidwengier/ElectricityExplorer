namespace ElectricityExplorer.Core.Analysis;

public sealed record TimeOfUseRate(
    string Name,
    decimal CentsPerKwh,
    TimeOnly Start,
    TimeOnly End,
    double? MaximumKwh = null,
    decimal? AboveLimitCentsPerKwh = null);
