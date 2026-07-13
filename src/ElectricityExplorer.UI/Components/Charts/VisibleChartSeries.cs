namespace ElectricityExplorer.UI.Components.Charts;

internal sealed record VisibleChartSeries(
    ChartSeries Series,
    IReadOnlyList<ChartPoint> Points);
