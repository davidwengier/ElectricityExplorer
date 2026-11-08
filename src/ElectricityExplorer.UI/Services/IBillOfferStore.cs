using ElectricityExplorer.UI.Models;

namespace ElectricityExplorer.UI.Services;

public interface IBillOfferStore
{
    IReadOnlyList<BillOfferSettings> Load();

    void Save(IReadOnlyList<BillOfferSettings> offers);
}
