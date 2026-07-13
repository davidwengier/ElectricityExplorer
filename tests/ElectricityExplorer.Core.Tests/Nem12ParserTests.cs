using System.Globalization;
using System.Text;
using ElectricityExplorer.Core.Models;
using ElectricityExplorer.Core.Nem12;

namespace ElectricityExplorer.Core.Tests;

public sealed class Nem12ParserTests
{
    [Fact]
    public async Task ParseAsync_ParsesImportAndExportChannels()
    {
        var content = string.Join(
            "\r\n",
            "100,NEM12,202507010900,RETAILER,CUSTOMER",
            "200,41020000000,E1B1,1,E1,IMPORT,METER-1,kWh,30,",
            IntervalRecord("20250630", 48, "0.5"),
            "200,41020000000,E1B1,2,B1,EXPORT,METER-1,kWh,30,",
            IntervalRecord("20250630", 48, "0.1"),
            "900");

        var dataset = await ParseAsync(content);

        Assert.Equal(2, dataset.Channels.Count);
        Assert.Equal(96, dataset.Readings.Count);
        Assert.Equal(
            EnergyFlowDirection.Import,
            dataset.Channels.Single(channel => channel.NmiSuffix == "E1").Direction);
        Assert.Equal(
            EnergyFlowDirection.Export,
            dataset.Channels.Single(channel => channel.NmiSuffix == "B1").Direction);
        Assert.Equal(24, dataset.Readings.Where(reading => reading.EnergyKwh == 0.5).Sum(reading => reading.EnergyKwh));
        Assert.Equal(new DateTime(2025, 6, 30, 23, 30, 0), dataset.Readings.Max(reading => reading.Timestamp));
    }

    [Fact]
    public async Task ParseAsync_ConvertsWhAndAcceptsQuotedMetadata()
    {
        var content = string.Join(
            "\n",
            "100,NEM12,202507010900,RETAILER,CUSTOMER",
            "200,41020000000,E1,1,E1,IMPORT,METER-1,Wh,60,",
            IntervalRecord("20250630", 24, "500"),
            "400,1,2,A,79,\"Estimated, retailer supplied\"",
            "900");

        var dataset = await ParseAsync(content);

        Assert.Equal(12, dataset.Readings.Sum(reading => reading.EnergyKwh), precision: 6);
    }

    [Fact]
    public async Task ParseAsync_ReplacesEarlierReadingsForTheSameChannelAndTime()
    {
        var content = string.Join(
            "\n",
            "100,NEM12,202507010900,RETAILER,CUSTOMER",
            "200,41020000000,E1,1,E1,IMPORT,METER-1,kWh,60,",
            IntervalRecord("20250630", 24, "1"),
            IntervalRecord("20250630", 24, "2"),
            "900");

        var dataset = await ParseAsync(content);

        Assert.Equal(24, dataset.Readings.Count);
        Assert.All(dataset.Readings, reading => Assert.Equal(2, reading.EnergyKwh));
    }

    [Fact]
    public async Task ParseAsync_AcceptsSupplierExportWithoutEnvelopeRecords()
    {
        var content = string.Join(
            "\n",
            "200,41020000000,B1E1,B1,B1,,METER-1,KWH,60,",
            IntervalRecord("20250630", 24, "0.1"),
            "400,1,12,A,,",
            "200,41020000000,B1E1,E1,E1,,METER-1,KWH,60,",
            IntervalRecord("20250630", 24, "0.5"));

        var dataset = await ParseAsync(content);

        Assert.Equal(2, dataset.Channels.Count);
        Assert.Equal(48, dataset.Readings.Count);
        Assert.Contains(
            dataset.Warnings,
            warning => warning.Contains("100 header", StringComparison.Ordinal));
        Assert.Contains(
            dataset.Warnings,
            warning => warning.Contains("900 end", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParseAsync_RejectsAnIncompleteIntervalRecord()
    {
        var content = string.Join(
            "\n",
            "100,NEM12,202507010900,RETAILER,CUSTOMER",
            "200,41020000000,E1,1,E1,IMPORT,METER-1,kWh,30,",
            "300,20250630,1,2,3",
            "900");

        var exception = await Assert.ThrowsAsync<Nem12ParseException>(() => ParseAsync(content));

        Assert.Contains("requires 48 interval values", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<ElectricityDataset> ParseAsync(string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await new Nem12Parser().ParseAsync(stream, "meter-data.csv");
    }

    private static string IntervalRecord(string date, int count, string value)
    {
        var values = Enumerable.Repeat(value, count);
        return string.Join(
            ",",
            new[] { "300", date }
                .Concat(values)
                .Concat(["A", "", "", "20250701090000", "20250701090100"]));
    }
}
