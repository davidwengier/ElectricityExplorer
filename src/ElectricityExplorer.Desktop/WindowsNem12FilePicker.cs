using ElectricityExplorer.UI.Services;

namespace ElectricityExplorer.Desktop;

public sealed class WindowsNem12FilePicker : INem12FilePicker
{
    public Task<Nem12FileSelection?> PickAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            CheckPathExists = true,
            Filter = "NEM12 files (*.csv;*.nem12)|*.csv;*.nem12|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = "Choose an NEM12 file"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return Task.FromResult<Nem12FileSelection?>(null);
        }

        var file = new FileInfo(dialog.FileName);
        Stream stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        return Task.FromResult<Nem12FileSelection?>(
            new Nem12FileSelection(file.Name, file.Length, stream));
    }
}
