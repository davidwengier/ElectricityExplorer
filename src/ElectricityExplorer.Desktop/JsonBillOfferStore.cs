using System.Text.Json;
using ElectricityExplorer.UI.Models;
using ElectricityExplorer.UI.Services;

namespace ElectricityExplorer.Desktop;

internal sealed class JsonBillOfferStore : IBillOfferStore
{
    private readonly JsonSettingsFile<IReadOnlyList<BillOfferSettings>> _file;

    public JsonBillOfferStore(string path)
    {
        _file = new JsonSettingsFile<IReadOnlyList<BillOfferSettings>>(path);
    }

    public IReadOnlyList<BillOfferSettings> Load()
    {
        var offers = _file.Load() ?? [];

        // Invalid tariffs can be unfinished edits, but missing objects aren't usable settings.
        if (offers.Any(offer => offer is null || offer.TimedRates.Any(rate => rate is null)))
        {
            throw new JsonException("The saved offers contain missing offer or period settings.");
        }

        return offers;
    }

    public void Save(IReadOnlyList<BillOfferSettings> offers) => _file.Save(offers);
}
