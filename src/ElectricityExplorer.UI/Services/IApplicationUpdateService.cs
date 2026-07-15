namespace ElectricityExplorer.UI.Services;

public interface IApplicationUpdateService
{
    bool CanUpdate { get; }

    string? CurrentVersion { get; }

    Task<ApplicationUpdate?> CheckForUpdateAsync();

    Task DownloadUpdateAsync(CancellationToken cancellationToken = default);

    void ApplyUpdateAndRestart();
}
