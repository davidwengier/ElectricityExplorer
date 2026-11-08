using System.Xml.Linq;
using ElectricityExplorer.Core.Analysis;
using ElectricityExplorer.UI.Components.Billing;
using ElectricityExplorer.UI.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElectricityExplorer.Desktop.Tests;

public sealed class BillEstimatorPanelTests
{
    [Fact]
    public async Task Render_ShowsRateColumnsWithSeparateAboveCapUsageAndCost()
    {
        var offer = BillOfferSettings.Create(1);
        offer.TimedRates.Add(new BillRateSettings
        {
            Name = "Allowance",
            CentsPerKwh = 0,
            Start = new TimeOnly(11, 0),
            End = new TimeOnly(14, 0),
            HasUsageCap = true,
            MaximumKwh = 15,
            AboveLimitCentsPerKwh = 30
        });
        offer.TimedRates.Add(new BillRateSettings
        {
            Name = "Peak",
            CentsPerKwh = 50,
            Start = new TimeOnly(15, 0),
            End = new TimeOnly(21, 0)
        });
        var date = new DateTime(2025, 1, 1);
        var profile = new[]
        {
            new SiteInterval(date.AddHours(10), 1, 2, 0),
            new SiteInterval(date.AddHours(11), 1, 10, 0),
            new SiteInterval(date.AddHours(12), 1, 8, 0),
            new SiteInterval(date.AddHours(18), 1, 1, 0),
            new SiteInterval(date.AddMonths(1).AddHours(11), 1, 2, 0)
        };

        var table = await RenderTableAsync(offer, profile);

        var headers = table.Element("thead")!.Element("tr")!.Elements("th").ToArray();
        Assert.Equal(10, headers.Length);
        Assert.Equal(
            ["Default rate", "Allowance", "Allowance (above cap)", "Peak"],
            headers.Skip(5).Take(4).Select(header => header.Element("span")!.Value));
        Assert.Equal("30.00 c/kWh", headers[7].Element("small")!.Value);
        var rows = table.Element("tbody")!.Elements("tr").ToArray();
        Assert.Equal(2, rows.Length);
        var january = rows[0].Elements().ToArray();
        Assert.Equal(headers.Length, january.Length);
        Assert.Equal("$3.00", january[1].Value);
        AssertRateCell(january[5], "$0.60", "2.0 kWh");
        AssertRateCell(january[6], "$0.00", "15.0 kWh");
        AssertRateCell(january[7], "$0.90", "3.0 kWh");
        AssertRateCell(january[8], "$0.50", "1.0 kWh");

        // An unused above-cap rate still needs a cell, otherwise later columns would shift across months.
        var february = rows[1].Elements().ToArray();
        Assert.Equal(headers.Length, february.Length);
        AssertRateCell(february[5], "$0.00", "0.0 kWh");
        AssertRateCell(february[6], "$0.00", "2.0 kWh");
        AssertRateCell(february[7], "$0.00", "0.0 kWh");
        AssertRateCell(february[8], "$0.00", "0.0 kWh");
    }

    [Fact]
    public async Task Render_UncappedPeriodHasNoAboveCapColumn()
    {
        var offer = BillOfferSettings.Create(1);
        offer.TimedRates.Add(new BillRateSettings
        {
            Name = "Peak",
            CentsPerKwh = 50,
            Start = new TimeOnly(15, 0),
            End = new TimeOnly(21, 0)
        });
        var date = new DateTime(2025, 1, 1);

        var table = await RenderTableAsync(
            offer,
            [
                new SiteInterval(date.AddHours(10), 1, 3, 0),
                new SiteInterval(date.AddHours(18), 1, 2, 0)
            ]);

        var headers = table.Element("thead")!.Element("tr")!.Elements("th").ToArray();
        Assert.Equal(8, headers.Length);
        Assert.DoesNotContain("above cap", table.ToString());
        var cells = table.Element("tbody")!.Element("tr")!.Elements().ToArray();
        AssertRateCell(cells[5], "$0.90", "3.0 kWh");
        AssertRateCell(cells[6], "$1.00", "2.0 kWh");
    }

    private static void AssertRateCell(XElement cell, string cost, string energy)
    {
        Assert.Equal(cost, cell.Element("span")!.Value);
        Assert.Equal(energy, cell.Element("small")!.Value);
    }

    private static async Task<XElement> RenderTableAsync(BillOfferSettings offer, IReadOnlyList<SiteInterval> profile)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BillEstimatorPanel.Offers)] = new List<BillOfferSettings> { offer },
                [nameof(BillEstimatorPanel.Profile)] = profile
            });
            var component = await renderer.RenderComponentAsync<BillEstimatorPanel>(parameters);
            var html = component.ToHtmlString();
            var start = html.IndexOf("<table>", StringComparison.Ordinal);
            var end = html.IndexOf("</table>", start, StringComparison.Ordinal) + "</table>".Length;
            return XElement.Parse(html[start..end]);
        });
    }
}
