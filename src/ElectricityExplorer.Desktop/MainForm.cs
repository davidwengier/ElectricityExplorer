using ElectricityExplorer.UI;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace ElectricityExplorer.Desktop;

public sealed class MainForm : Form
{
    private const string ApplicationIconResourceName =
        "ElectricityExplorer.Desktop.Assets.ElectricityExplorer.ico";

    private readonly WindowStateStore _windowStateStore;
    private FormWindowState _lastNonMinimizedState = FormWindowState.Normal;

    internal MainForm(IServiceProvider services, WindowStateStore windowStateStore)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(windowStateStore);
        _windowStateStore = windowStateStore;

        Text = "Electricity Explorer";
        Icon = LoadApplicationIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1400, 900);
        RestoreWindowState();

        var blazorWebView = new EmbeddedBlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot\\index.html",
            Services = services
        };
        blazorWebView.RootComponents.Add<App>("#app");

        Controls.Add(blazorWebView);
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(
            ApplicationIconResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded application icon '{ApplicationIconResourceName}' was not found.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState != FormWindowState.Minimized)
        {
            _lastNonMinimizedState = WindowState;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var state = WindowState == FormWindowState.Minimized
            ? _lastNonMinimizedState
            : WindowState;
        var bounds = state == FormWindowState.Normal
            ? Bounds
            : RestoreBounds;

        _windowStateStore.Save(new SavedWindowState(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            state == FormWindowState.Maximized));

        base.OnFormClosing(e);
    }

    private void RestoreWindowState()
    {
        var saved = _windowStateStore.Load();
        if (saved is null)
        {
            WindowState = FormWindowState.Normal;
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = FitToVisibleScreen(new Rectangle(
            saved.X,
            saved.Y,
            saved.Width,
            saved.Height));
        WindowState = saved.IsMaximized
            ? FormWindowState.Maximized
            : FormWindowState.Normal;
        _lastNonMinimizedState = WindowState;
    }

    private Rectangle FitToVisibleScreen(Rectangle bounds)
    {
        var workingArea = Screen.FromRectangle(bounds).WorkingArea;
        var width = Math.Min(
            workingArea.Width,
            Math.Max(bounds.Width, MinimumSize.Width));
        var height = Math.Min(
            workingArea.Height,
            Math.Max(bounds.Height, MinimumSize.Height));
        var x = Math.Clamp(bounds.X, workingArea.Left, workingArea.Right - width);
        var y = Math.Clamp(bounds.Y, workingArea.Top, workingArea.Bottom - height);

        return new Rectangle(x, y, width, height);
    }
}
