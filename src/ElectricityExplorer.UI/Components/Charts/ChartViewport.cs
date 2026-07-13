namespace ElectricityExplorer.UI.Components.Charts;

public readonly record struct ChartViewport(double Minimum, double Maximum)
{
    public double Range => Maximum - Minimum;
}
