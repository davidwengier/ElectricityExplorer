namespace ElectricityExplorer.UI.Models;

public sealed class ApplicationSettings
{
    public AnalysisSettings Analysis { get; init; } = new();

    public BatterySurvivalSettings BatterySurvival { get; init; } = new();

    public string? SelectedDatasetId { get; init; }

    public string? SelectedNmi { get; init; }

    public DateOnly? DetailDate { get; init; }

    public ScenarioTab ActiveScenarioTab { get; init; } = ScenarioTab.BatterySurvival;
}
