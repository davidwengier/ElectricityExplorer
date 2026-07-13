namespace ElectricityExplorer.UI.Components.Charts;

internal static class ChartViewportMath
{
    public static double CalculateMinimumRange(
        IEnumerable<double> values,
        double fullRange)
    {
        var orderedValues = values
            .Distinct()
            .Order()
            .ToArray();
        if (orderedValues.Length < 2)
        {
            return fullRange;
        }

        var smallestStep = double.MaxValue;
        for (var index = 1; index < orderedValues.Length; index++)
        {
            smallestStep = Math.Min(
                smallestStep,
                orderedValues[index] - orderedValues[index - 1]);
        }

        return Math.Min(fullRange, smallestStep * 4d);
    }

    public static ChartViewport Clamp(
        ChartViewport viewport,
        ChartViewport fullViewport)
    {
        if (!double.IsFinite(viewport.Minimum)
            || !double.IsFinite(viewport.Maximum)
            || viewport.Maximum <= viewport.Minimum)
        {
            return fullViewport;
        }

        var range = Math.Min(viewport.Range, fullViewport.Range);
        var minimum = viewport.Minimum;
        var maximum = minimum + range;

        if (minimum < fullViewport.Minimum)
        {
            minimum = fullViewport.Minimum;
            maximum = minimum + range;
        }

        if (maximum > fullViewport.Maximum)
        {
            maximum = fullViewport.Maximum;
            minimum = maximum - range;
        }

        return new ChartViewport(minimum, maximum);
    }

    public static ChartViewport SnapToValues(
        ChartViewport viewport,
        IEnumerable<double> values)
    {
        var orderedValues = values
            .Distinct()
            .Order()
            .ToArray();
        if (orderedValues.Length < 2)
        {
            return viewport;
        }

        var first = Array.FindIndex(
            orderedValues,
            value => value >= viewport.Minimum);
        var last = Array.FindLastIndex(
            orderedValues,
            value => value <= viewport.Maximum);

        return first >= 0 && last > first
            ? new ChartViewport(orderedValues[first], orderedValues[last])
            : viewport;
    }

    public static bool AreEqual(ChartViewport left, ChartViewport right)
    {
        var tolerance = Math.Max(1, right.Range) * 0.000000001;
        return Math.Abs(left.Minimum - right.Minimum) <= tolerance
               && Math.Abs(left.Maximum - right.Maximum) <= tolerance;
    }
}
