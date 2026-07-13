using System.Diagnostics;
using System.Text.Json;

namespace ElectricityExplorer.Desktop;

internal sealed class WindowStateStore
{
    private readonly string _path;

    public WindowStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public SavedWindowState? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<SavedWindowState>(stream);
        }
        catch (JsonException exception)
        {
            Trace.TraceWarning($"Window state could not be read from '{_path}': {exception.Message}");
            return null;
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Window state could not be opened from '{_path}': {exception.Message}");
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Window state could not be accessed at '{_path}': {exception.Message}");
            return null;
        }
    }

    public void Save(SavedWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, state);
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Window state could not be saved to '{_path}': {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Window state could not be saved to '{_path}': {exception.Message}");
        }
    }
}
