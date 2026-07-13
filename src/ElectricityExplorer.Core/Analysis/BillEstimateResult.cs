namespace ElectricityExplorer.Core.Analysis;

public sealed class BillEstimateResult
{
    public IReadOnlyList<MonthlyBillEstimate> Months { get; init; } = [];

    public decimal TotalCostDollars => Months.Sum(month => month.TotalCostDollars);
}
