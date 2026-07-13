using ElectricityExplorer.Core.Models;
using ElectricityExplorer.Storage.Sqlite;

namespace ElectricityExplorer.Storage.Sqlite.Tests;

public sealed class SqliteDatasetStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ElectricityExplorer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsDataset()
    {
        using var store = CreateStore();
        var dataset = CreateDataset();

        await store.SaveAsync(dataset);

        var loaded = await store.GetAsync(dataset.Id);
        var summary = Assert.Single(await store.GetSummariesAsync());

        Assert.NotNull(loaded);
        Assert.Equal(dataset.Name, loaded.Name);
        Assert.Equal(dataset.ImportedAt, loaded.ImportedAt);
        Assert.Equal(dataset.Channels.Count, loaded.Channels.Count);
        Assert.Equal(dataset.Readings.Count, loaded.Readings.Count);
        Assert.Equal(dataset.Warnings, loaded.Warnings);
        Assert.Equal(EnergyFlowDirection.Export, loaded.Channels[1].Direction);
        Assert.Equal(
            0.75,
            loaded.Readings.Single(reading => reading.ChannelId == "2").EnergyKwh);

        Assert.Equal(dataset.Id, summary.Id);
        Assert.Equal(2, summary.ChannelCount);
        Assert.Equal(3, summary.ReadingCount);
        Assert.Equal(["41020000000"], summary.Nmis);
        Assert.Equal(new DateTime(2025, 1, 1), summary.Start);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 30, 0), summary.End);
    }

    [Fact]
    public async Task Save_ReplacesExistingDatasetAndDeleteRemovesIt()
    {
        using var store = CreateStore();
        var dataset = CreateDataset();
        await store.SaveAsync(dataset);

        dataset.Name = "Updated name";
        dataset.Readings.RemoveAt(0);
        await store.SaveAsync(dataset);

        var loaded = await store.GetAsync(dataset.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated name", loaded.Name);
        Assert.Equal(2, loaded.Readings.Count);

        await store.DeleteAsync(dataset.Id);

        Assert.Null(await store.GetAsync(dataset.Id));
        Assert.Empty(await store.GetSummariesAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SqliteDatasetStore CreateStore() =>
        new(Path.Combine(_directory, "test.db"));

    private static ElectricityDataset CreateDataset()
    {
        var timestamp = new DateTime(2025, 1, 1);
        return new ElectricityDataset
        {
            Id = "dataset-1",
            Name = "Test dataset",
            SourceFileName = "test.csv",
            ImportedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
            Warnings = ["Header omitted"],
            Channels =
            [
                new Nem12Channel
                {
                    Id = "1",
                    Nmi = "41020000000",
                    NmiConfiguration = "B1E1",
                    RegisterId = "E1",
                    NmiSuffix = "E1",
                    MeterSerialNumber = "METER-1",
                    Unit = "KWH",
                    IntervalMinutes = 30,
                    Direction = EnergyFlowDirection.Import
                },
                new Nem12Channel
                {
                    Id = "2",
                    Nmi = "41020000000",
                    NmiConfiguration = "B1E1",
                    RegisterId = "B1",
                    NmiSuffix = "B1",
                    MeterSerialNumber = "METER-1",
                    Unit = "KWH",
                    IntervalMinutes = 30,
                    Direction = EnergyFlowDirection.Export
                }
            ],
            Readings =
            [
                new Nem12Reading
                {
                    ChannelId = "1",
                    Timestamp = timestamp,
                    EnergyKwh = 1.25
                },
                new Nem12Reading
                {
                    ChannelId = "2",
                    Timestamp = timestamp,
                    EnergyKwh = 0.75
                },
                new Nem12Reading
                {
                    ChannelId = "1",
                    Timestamp = timestamp.AddMinutes(30),
                    EnergyKwh = 1.5
                }
            ]
        };
    }
}
