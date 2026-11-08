using ElectricityExplorer.Core.Models;
using ElectricityExplorer.Core.Storage;
using ElectricityExplorer.Desktop;
using ElectricityExplorer.Storage.Sqlite;
using ElectricityExplorer.UI.Models;
using ElectricityExplorer.UI.Pages;
using ElectricityExplorer.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ElectricityExplorer.Desktop.Tests;

public sealed class HomeSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ElectricityExplorer.Tests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task OpeningApp_RestoresInputsDatasetNmiTabAndDetailDay()
    {
        var settings = new ApplicationSettings
        {
            Analysis = new AnalysisSettings { BatteryCapacityKwh = 12.5, ImportTariffPerKwh = 0.42 },
            SelectedDatasetId = "selected",
            SelectedNmi = "200",
            ActiveScenarioTab = ScenarioTab.AdvancedAnalysis,
            DetailDate = new DateOnly(2025, 1, 2)
        };

        var html = await RenderHomeAsync(settings);

        Assert.Contains("id=\"dataset-select\" value=\"selected\"", html);
        Assert.Contains("""<select value="200">""", html);
        Assert.Contains("aria-label=\"Detailed battery and solar analysis\"", html);
        Assert.Contains("value=\"12.5\"", html);
        Assert.Contains("value=\"0.42\"", html);
        Assert.Contains("value=\"2025-01-02\"", html);
        Assert.DoesNotContain("Something needs attention", html);
    }

    [Fact]
    public async Task OpeningApp_RestoresSurvivalInputs()
    {
        var settings = new ApplicationSettings
        {
            BatterySurvival = new BatterySurvivalSettings
            {
                AdditionalBatteryCapacityKwh = 16.5,
                AdditionalSolarKw = 2.5,
                FreeTariffStartHour = 10.5,
                FreeTariffEndHour = 15.5,
                BatteryFullAt = new TimeOnly(16, 30)
            },
            SelectedDatasetId = "selected"
        };

        var html = await RenderHomeAsync(settings);

        Assert.Contains("value=\"16.5\"", html);
        Assert.Contains("value=\"2.5\"", html);
        Assert.Contains("value=\"10.5\"", html);
        Assert.Contains("value=\"15.5\"", html);
        Assert.Contains("value=\"16:30:00\"", html);
        Assert.DoesNotContain("Something needs attention", html);
    }

    [Fact]
    public async Task OpeningApp_MissingDatasetFallsBackToAvailableDatasetAndDay()
    {
        var settings = new ApplicationSettings
        {
            SelectedDatasetId = "removed",
            SelectedNmi = "removed-nmi",
            ActiveScenarioTab = ScenarioTab.AdvancedAnalysis,
            DetailDate = new DateOnly(2040, 1, 1)
        };

        var html = await RenderHomeAsync(settings);

        Assert.Contains("id=\"dataset-select\" value=\"newest\"", html);
        Assert.Contains("""<select value="100">""", html);
        Assert.Contains("value=\"2025-01-01\"", html);
        Assert.DoesNotContain("value=\"2040-01-01\"", html);
        Assert.DoesNotContain("Something needs attention", html);
    }

    [Fact]
    public async Task OpeningApp_MissingNmiAndOutOfRangeDayFallBackWithinSavedDataset()
    {
        var settings = new ApplicationSettings
        {
            SelectedDatasetId = "selected",
            SelectedNmi = "removed-nmi",
            ActiveScenarioTab = ScenarioTab.AdvancedAnalysis,
            DetailDate = new DateOnly(2040, 1, 1)
        };

        var html = await RenderHomeAsync(settings);

        Assert.Contains("id=\"dataset-select\" value=\"selected\"", html);
        Assert.Contains("""<select value="100">""", html);
        Assert.Contains("value=\"2025-01-01\"", html);
        Assert.DoesNotContain("Something needs attention", html);
    }

    private async Task<string> RenderHomeAsync(ApplicationSettings settings)
    {
        var settingsStore = new JsonApplicationSettingsStore(SettingsPath);
        settingsStore.Save(settings);
        var savedJson = File.ReadAllText(SettingsPath);
        using var datasetStore = new SqliteDatasetStore(Path.Combine(_directory, "test.db"));
        await datasetStore.SaveAsync(CreateDataset("selected", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await datasetStore.SaveAsync(CreateDataset("newest", new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        var desktopServices = new HeadlessDesktopServices();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDatasetStore>(datasetStore);
        services.AddSingleton<IApplicationSettingsStore>(settingsStore);
        services.AddSingleton<IBillOfferStore>(new JsonBillOfferStore(Path.Combine(_directory, "bill-offers.json")));
        services.AddSingleton<INem12FilePicker>(desktopServices);
        services.AddSingleton<IApplicationUpdateService>(desktopServices);
        services.AddSingleton<IJSRuntime>(desktopServices);
        using var serviceProvider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider, serviceProvider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<Home>();
            return component.ToHtmlString();
        });

        // Startup can choose a fallback without overwriting what was saved or creating default offers.
        Assert.Equal(savedJson, File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(Path.Combine(_directory, "bill-offers.json")));
        return html;
    }

    private static ElectricityDataset CreateDataset(string id, DateTimeOffset importedAt) =>
        new()
        {
            Id = id,
            Name = id,
            SourceFileName = $"{id}.csv",
            ImportedAt = importedAt,
            Channels =
            [
                new Nem12Channel { Id = "1", Nmi = "100", NmiSuffix = "E1", Unit = "KWH", IntervalMinutes = 60, Direction = EnergyFlowDirection.Import },
                new Nem12Channel { Id = "2", Nmi = "200", NmiSuffix = "E1", Unit = "KWH", IntervalMinutes = 60, Direction = EnergyFlowDirection.Import }
            ],
            Readings =
            [
                new Nem12Reading { ChannelId = "1", Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), EnergyKwh = 1 },
                new Nem12Reading { ChannelId = "1", Timestamp = new DateTime(2025, 1, 2, 12, 0, 0), EnergyKwh = 1 },
                new Nem12Reading { ChannelId = "2", Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), EnergyKwh = 1 },
                new Nem12Reading { ChannelId = "2", Timestamp = new DateTime(2025, 1, 2, 12, 0, 0), EnergyKwh = 1 }
            ]
        };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
