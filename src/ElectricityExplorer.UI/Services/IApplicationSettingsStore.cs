using ElectricityExplorer.UI.Models;

namespace ElectricityExplorer.UI.Services;

public interface IApplicationSettingsStore
{
    ApplicationSettings? Load();

    void Save(ApplicationSettings settings);
}
