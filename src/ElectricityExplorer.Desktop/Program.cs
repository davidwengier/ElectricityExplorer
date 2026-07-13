using ElectricityExplorer.Core.Storage;
using ElectricityExplorer.Storage.Sqlite;
using ElectricityExplorer.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ElectricityExplorer.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElectricityExplorer");
        var databasePath = Environment.GetEnvironmentVariable("ELECTRICITY_EXPLORER_DATABASE");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            databasePath = Path.Combine(appDataDirectory, "electricity-explorer.db");
        }
        var windowStatePath = Path.Combine(appDataDirectory, "window-state.json");

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<IDatasetStore>(new SqliteDatasetStore(databasePath));
        services.AddSingleton<INem12FilePicker, WindowsNem12FilePicker>();

        using var serviceProvider = services.BuildServiceProvider();
        Application.Run(new MainForm(
            serviceProvider,
            new WindowStateStore(windowStatePath)));
    }
}