using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.FileProviders;

namespace ElectricityExplorer.Desktop;

public sealed class EmbeddedBlazorWebView : BlazorWebView
{
    public override IFileProvider CreateFileProvider(string contentRootDir) =>
        new ManifestEmbeddedFileProvider(
            typeof(EmbeddedBlazorWebView).Assembly,
            "wwwroot");
}
