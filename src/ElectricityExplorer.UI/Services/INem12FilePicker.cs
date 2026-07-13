namespace ElectricityExplorer.UI.Services;

public interface INem12FilePicker
{
    Task<Nem12FileSelection?> PickAsync();
}
