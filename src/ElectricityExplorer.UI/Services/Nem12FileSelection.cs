namespace ElectricityExplorer.UI.Services;

public sealed class Nem12FileSelection(
    string name,
    long size,
    Stream stream) : IAsyncDisposable
{
    public string Name { get; } = name;

    public long Size { get; } = size;

    public Stream Stream { get; } = stream;

    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
