namespace Sims4ModManager.App;

/// <summary>
/// Shown immediately at startup while <see cref="MainWindow"/> is constructed - which does a full
/// mod scan synchronously and can take a while. Without this, the app looks like it failed to
/// launch during that time.
/// </summary>
public partial class SplashWindow
{
    public SplashWindow() => InitializeComponent();
}
