using ElectricityExplorer.UI.Services;
using Microsoft.JSInterop;

namespace ElectricityExplorer.Desktop.Tests;

internal sealed class HeadlessDesktopServices : INem12FilePicker, IApplicationUpdateService, IJSRuntime
{
    public bool CanUpdate => false;

    public string? CurrentVersion => null;

    // Static rendering must not open native dialogs, check for updates or run browser scripts.
    public Task<Nem12FileSelection?> PickAsync() => throw new NotSupportedException();

    public Task<ApplicationUpdate?> CheckForUpdateAsync() => throw new NotSupportedException();

    public Task DownloadUpdateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void ApplyUpdateAndRestart() => throw new NotSupportedException();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        throw new NotSupportedException();
}
