using ElectricityExplorer.Core.Models;

namespace ElectricityExplorer.Core.Storage;

public interface IDatasetStore
{
    Task<IReadOnlyList<DatasetSummary>> GetSummariesAsync();

    Task<ElectricityDataset?> GetAsync(string id);

    Task SaveAsync(ElectricityDataset dataset);

    Task DeleteAsync(string id);

    Task<DatasetStorageStatus> GetStatusAsync();
}
