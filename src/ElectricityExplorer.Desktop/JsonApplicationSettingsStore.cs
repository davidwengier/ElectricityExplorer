using System.Text.Json;
using ElectricityExplorer.UI.Models;
using ElectricityExplorer.UI.Services;

namespace ElectricityExplorer.Desktop;

internal sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private readonly JsonSettingsFile<ApplicationSettings> _file;

    public JsonApplicationSettingsStore(string path)
    {
        _file = new JsonSettingsFile<ApplicationSettings>(path);
    }

    public ApplicationSettings? Load()
    {
        var settings = _file.Load();
        if (settings is null)
        {
            return null;
        }

        if (settings.Analysis is null || settings.BatterySurvival is null
            || !Enum.IsDefined(settings.ActiveScenarioTab))
        {
            throw new JsonException("The saved settings contain missing scenarios or an unknown tab.");
        }

        // Unfinished analysis inputs can be restored, but invalid slider values would break the survival view.
        try
        {
            var survival = settings.BatterySurvival;
            if (!double.IsFinite(survival.FreeTariffStartHour) || !double.IsFinite(survival.FreeTariffEndHour)
                || survival.FreeTariffStartHour < 0 || survival.FreeTariffEndHour > 23.5
                || survival.FreeTariffEndHour - survival.FreeTariffStartHour < 0.5)
            {
                throw new ArgumentException("The saved free tariff window is outside the slider range.");
            }

            survival.ToOptions().Validate();
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("The saved battery-survival settings cannot be used.", exception);
        }

        return settings;
    }

    public void Save(ApplicationSettings settings) => _file.Save(settings);
}
