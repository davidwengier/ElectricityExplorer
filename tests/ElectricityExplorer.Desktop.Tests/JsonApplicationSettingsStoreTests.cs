using System.Text.Json;
using ElectricityExplorer.Desktop;
using ElectricityExplorer.UI.Models;

namespace ElectricityExplorer.Desktop.Tests;

public sealed class JsonApplicationSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ElectricityExplorer.Tests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Load_MissingFileReturnsNoSettings()
    {
        Assert.Null(new JsonApplicationSettingsStore(SettingsPath).Load());
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void SaveAndLoad_RestoresBothScenariosAndViewSelections()
    {
        var settings = new ApplicationSettings
        {
            Analysis = new AnalysisSettings
            {
                BatteryCapacityKwh = 12.5,
                BatteryPowerKw = 4.5,
                RoundTripEfficiencyPercent = 85,
                ReservePercent = 15,
                InitialChargePercent = 65,
                AdditionalSolarKw = 3.5,
                SolarYieldKwhPerKwDay = 4.2,
                ImportTariffPerKwh = 0.42,
                FeedInTariffPerKwh = 0.07,
                MaximumComparedBatteryKwh = 25
            },
            BatterySurvival = new BatterySurvivalSettings
            {
                AdditionalBatteryCapacityKwh = 16.5,
                AdditionalSolarKw = 2.5,
                FreeTariffStartHour = 10.5,
                FreeTariffEndHour = 15.5,
                BatteryFullAt = new TimeOnly(16, 30),
                SolarYieldKwhPerKwDay = 3.8
            },
            SelectedDatasetId = "dataset-2",
            SelectedNmi = "200",
            DetailDate = new DateOnly(2025, 1, 2),
            ActiveScenarioTab = ScenarioTab.AdvancedAnalysis
        };
        new JsonApplicationSettingsStore(SettingsPath).Save(settings);

        var restored = new JsonApplicationSettingsStore(SettingsPath).Load();

        Assert.NotNull(restored);
        Assert.Equal(12.5, restored.Analysis.BatteryCapacityKwh);
        Assert.Equal(4.5, restored.Analysis.BatteryPowerKw);
        Assert.Equal(85, restored.Analysis.RoundTripEfficiencyPercent);
        Assert.Equal(15, restored.Analysis.ReservePercent);
        Assert.Equal(65, restored.Analysis.InitialChargePercent);
        Assert.Equal(3.5, restored.Analysis.AdditionalSolarKw);
        Assert.Equal(4.2, restored.Analysis.SolarYieldKwhPerKwDay);
        Assert.Equal(0.42, restored.Analysis.ImportTariffPerKwh);
        Assert.Equal(0.07, restored.Analysis.FeedInTariffPerKwh);
        Assert.Equal(25, restored.Analysis.MaximumComparedBatteryKwh);
        Assert.Equal(16.5, restored.BatterySurvival.AdditionalBatteryCapacityKwh);
        Assert.Equal(2.5, restored.BatterySurvival.AdditionalSolarKw);
        Assert.Equal(10.5, restored.BatterySurvival.FreeTariffStartHour);
        Assert.Equal(15.5, restored.BatterySurvival.FreeTariffEndHour);
        Assert.Equal(new TimeOnly(16, 30), restored.BatterySurvival.BatteryFullAt);
        Assert.Equal(3.8, restored.BatterySurvival.SolarYieldKwhPerKwDay);
        Assert.Equal("dataset-2", restored.SelectedDatasetId);
        Assert.Equal("200", restored.SelectedNmi);
        Assert.Equal(new DateOnly(2025, 1, 2), restored.DetailDate);
        Assert.Equal(ScenarioTab.AdvancedAnalysis, restored.ActiveScenarioTab);

        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        var survival = document.RootElement.GetProperty(nameof(ApplicationSettings.BatterySurvival));
        Assert.False(survival.TryGetProperty(nameof(BatterySurvivalSettings.UsableBatteryCapacityKwh), out _));
        Assert.False(survival.TryGetProperty(nameof(BatterySurvivalSettings.FreeTariffStart), out _));
        Assert.False(survival.TryGetProperty(nameof(BatterySurvivalSettings.FreeTariffEnd), out _));
    }

    [Fact]
    public void SaveAndLoad_KeepsUnfinishedAnalysisInputs()
    {
        var settings = new ApplicationSettings
        {
            Analysis = new AnalysisSettings { ReservePercent = 20, InitialChargePercent = 10 }
        };
        var store = new JsonApplicationSettingsStore(SettingsPath);

        store.Save(settings);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(20, restored.Analysis.ReservePercent);
        Assert.Equal(10, restored.Analysis.InitialChargePercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => restored.Analysis.ToSimulationOptions().Validate());
    }

    [Fact]
    public void Load_MissingPropertiesUseDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"Analysis":{"BatteryCapacityKwh":12.5}}""");

        var settings = new JsonApplicationSettingsStore(SettingsPath).Load();

        Assert.NotNull(settings);
        Assert.Equal(12.5, settings.Analysis.BatteryCapacityKwh);
        Assert.Equal(5, settings.Analysis.BatteryPowerKw);
        Assert.Equal(new TimeOnly(14, 0), settings.BatterySurvival.BatteryFullAt);
        Assert.Equal(ScenarioTab.BatterySurvival, settings.ActiveScenarioTab);
    }

    [Fact]
    public void Load_CorruptJsonReportsFailureWithoutChangingFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{unfinished");

        Assert.Throws<JsonException>(() => new JsonApplicationSettingsStore(SettingsPath).Load());
        Assert.Equal("{unfinished", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Load_InvalidSurvivalWindowReportsFailureInsteadOfBreakingRendering()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"BatterySurvival":{"FreeTariffStartHour":25}}""");

        Assert.Throws<JsonException>(() => new JsonApplicationSettingsStore(SettingsPath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
