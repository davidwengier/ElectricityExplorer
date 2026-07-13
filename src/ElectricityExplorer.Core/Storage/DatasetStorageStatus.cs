namespace ElectricityExplorer.Core.Storage;

public sealed record DatasetStorageStatus(
    string Label,
    bool IsDurable,
    string FooterText);
