namespace ElectricityExplorer.Desktop;

internal sealed record SavedWindowState(
    int X,
    int Y,
    int Width,
    int Height,
    bool IsMaximized);
