namespace ElectricityExplorer.UI.Components.Charts;

public sealed record ChartSeries(
    string Name,
    string Color,
    IReadOnlyList<ChartPoint> Points,
    string? DashArray = null);
