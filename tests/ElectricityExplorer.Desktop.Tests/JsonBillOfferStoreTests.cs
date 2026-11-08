using System.Text.Json;
using ElectricityExplorer.Core.Analysis;
using ElectricityExplorer.Desktop;
using ElectricityExplorer.UI.Components.Billing;
using ElectricityExplorer.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElectricityExplorer.Desktop.Tests;

public sealed class JsonBillOfferStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ElectricityExplorer.Tests",
        Guid.NewGuid().ToString("N"));

    private string OffersPath => Path.Combine(_directory, "bill-offers.json");

    [Fact]
    public void Load_MissingFileReturnsNoOffers()
    {
        var store = new JsonBillOfferStore(OffersPath);

        Assert.Empty(store.Load());
        Assert.False(File.Exists(OffersPath));
    }

    [Fact]
    public void SaveAndLoad_RestoresAllOfferSettingsWithoutDatasetResults()
    {
        var offer = new BillOfferSettings
        {
            Name = "Weeknight offer",
            DailySupplyChargeCents = 123.45m,
            DefaultImportRateCentsPerKwh = 32.10m,
            FeedInTariffCentsPerKwh = 6.78m,
            Estimate = BillEstimator.Calculate(
                [new SiteInterval(new DateTime(2025, 1, 1, 12, 0, 0), 1, 2, 0)],
                new BillEstimateOptions()),
            Error = "An old dataset error"
        };
        offer.TimedRates.Add(new BillRateSettings
        {
            Name = "Overnight allowance",
            CentsPerKwh = 0,
            Start = new TimeOnly(22, 30),
            End = new TimeOnly(6, 45),
            HasUsageCap = true,
            MaximumKwh = 15.5,
            AboveLimitCentsPerKwh = 45.67m
        });
        offer.TimedRates.Add(new BillRateSettings
        {
            Name = "Peak",
            CentsPerKwh = 50.12m,
            Start = new TimeOnly(15, 0),
            End = new TimeOnly(21, 0),
            HasUsageCap = false,
            MaximumKwh = 8,
            AboveLimitCentsPerKwh = 60
        });
        var store = new JsonBillOfferStore(OffersPath);

        store.Save([offer, BillOfferSettings.Create(2)]);

        // A new store stands in for a restarted app, rather than reusing anything in memory.
        var loaded = new JsonBillOfferStore(OffersPath).Load();
        Assert.Equal(2, loaded.Count);
        var restored = loaded[0];
        Assert.Equal(offer.Name, restored.Name);
        Assert.Equal(offer.DailySupplyChargeCents, restored.DailySupplyChargeCents);
        Assert.Equal(offer.DefaultImportRateCentsPerKwh, restored.DefaultImportRateCentsPerKwh);
        Assert.Equal(offer.FeedInTariffCentsPerKwh, restored.FeedInTariffCentsPerKwh);
        Assert.Equal(2, restored.TimedRates.Count);
        var rate = restored.TimedRates[0];
        Assert.Equal("Overnight allowance", rate.Name);
        Assert.Equal(0, rate.CentsPerKwh);
        Assert.Equal(new TimeOnly(22, 30), rate.Start);
        Assert.Equal(new TimeOnly(6, 45), rate.End);
        Assert.True(rate.HasUsageCap);
        Assert.Equal(15.5, rate.MaximumKwh);
        Assert.Equal(45.67m, rate.AboveLimitCentsPerKwh);
        Assert.False(restored.TimedRates[1].HasUsageCap);
        Assert.Equal(8, restored.TimedRates[1].MaximumKwh);
        Assert.Equal(60, restored.TimedRates[1].AboveLimitCentsPerKwh);
        Assert.Equal("Offer 2", loaded[1].Name);
        Assert.Null(restored.Estimate);
        Assert.Null(restored.Error);

        using var document = JsonDocument.Parse(File.ReadAllText(OffersPath));
        var savedOffer = document.RootElement[0];
        Assert.False(savedOffer.TryGetProperty(nameof(BillOfferSettings.Estimate), out _));
        Assert.False(savedOffer.TryGetProperty(nameof(BillOfferSettings.Error), out _));
        Assert.False(savedOffer.TryGetProperty(nameof(BillOfferSettings.Id), out _));
        Assert.False(savedOffer.GetProperty(nameof(BillOfferSettings.TimedRates))[0]
            .TryGetProperty(nameof(BillRateSettings.Id), out _));
    }

    [Fact]
    public void Save_ReplacesOffersAndPeriodsAfterEditsAndRemovals()
    {
        var store = new JsonBillOfferStore(OffersPath);
        var offer = BillOfferSettings.Create(1);
        offer.TimedRates.Add(BillRateSettings.Create(0));
        store.Save([offer, BillOfferSettings.Create(2)]);

        offer.Name = "Renamed offer";
        offer.DefaultImportRateCentsPerKwh = 40;
        offer.TimedRates.Clear();
        store.Save([offer]);

        var restored = Assert.Single(new JsonBillOfferStore(OffersPath).Load());
        Assert.Equal("Renamed offer", restored.Name);
        Assert.Equal(40, restored.DefaultImportRateCentsPerKwh);
        Assert.Empty(restored.TimedRates);
        Assert.Equal([OffersPath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void Save_KeepsUnfinishedTariffEdits()
    {
        var store = new JsonBillOfferStore(OffersPath);
        var offer = BillOfferSettings.Create(1);
        offer.TimedRates.Add(BillRateSettings.Create(0));
        offer.TimedRates.Add(BillRateSettings.Create(1));

        store.Save([offer]);

        var restored = Assert.Single(store.Load());
        Assert.Equal(2, restored.TimedRates.Count);
        Assert.Throws<ArgumentException>(() => restored.ToOptions().Validate());
    }

    [Fact]
    public async Task Load_RecalculatesForCurrentDatasetWithoutSavingWhenEstimatorOpens()
    {
        var store = new JsonBillOfferStore(OffersPath);
        store.Save([new BillOfferSettings { Name = "Stored offer" }]);
        var original = File.ReadAllText(OffersPath);
        var offers = store.Load().ToList();
        var saveCount = 0;
        var offersChanged = EventCallback.Factory.Create(this, () =>
        {
            saveCount++;
            store.Save(offers);
        });
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            // Opening the tab against each dataset should recalculate, not write back to the settings file.
            await RenderForImportAsync(2);
            Assert.Equal(0.60m, offers[0].Estimate!.Months[0].UsageChargeDollars);
            await RenderForImportAsync(3);
            Assert.Equal(0.90m, offers[0].Estimate!.Months[0].UsageChargeDollars);
        });

        Assert.Equal(0, saveCount);
        Assert.Equal(original, File.ReadAllText(OffersPath));

        async Task RenderForImportAsync(double importKwh)
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BillEstimatorPanel.Offers)] = offers,
                [nameof(BillEstimatorPanel.OffersChanged)] = offersChanged,
                [nameof(BillEstimatorPanel.Profile)] = new[]
                {
                    new SiteInterval(new DateTime(2025, 1, 1, 12, 0, 0), 1, importKwh, 0)
                }
            });
            var component = await renderer.RenderComponentAsync<BillEstimatorPanel>(parameters);
            Assert.Contains("Stored offer", component.ToHtmlString());
        }
    }

    [Fact]
    public void Save_FailedReplacementPreservesPreviousOffersAndCleansUpTemporaryFile()
    {
        var store = new JsonBillOfferStore(OffersPath);
        store.Save([BillOfferSettings.Create(1)]);
        var original = File.ReadAllText(OffersPath);

        // Denying deletion prevents the atomic replacement without needing special filesystem permissions.
        using (var lockedFile = new FileStream(OffersPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Record.Exception(() => store.Save([BillOfferSettings.Create(2)]));
            Assert.True(exception is IOException or UnauthorizedAccessException);
        }

        Assert.Equal(original, File.ReadAllText(OffersPath));
        Assert.Equal([OffersPath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_CorruptJsonReportsFailureWithoutChangingFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(OffersPath, "[unfinished");
        var store = new JsonBillOfferStore(OffersPath);

        Assert.Throws<JsonException>(() => store.Load());

        Assert.Equal("[unfinished", File.ReadAllText(OffersPath));
    }

    [Fact]
    public void Load_MissingOfferObjectReportsFailure()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(OffersPath, "[null]");
        var store = new JsonBillOfferStore(OffersPath);

        Assert.Throws<JsonException>(() => store.Load());
    }

    [Fact]
    public void Load_MissingPeriodObjectReportsFailure()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(OffersPath, """[{"TimedRates":[null]}]""");
        var store = new JsonBillOfferStore(OffersPath);

        Assert.Throws<JsonException>(() => store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
