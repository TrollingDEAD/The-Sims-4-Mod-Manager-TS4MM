using System.Windows.Media.Animation;

namespace Sims4ModManager.App;

/// <summary>
/// Shown immediately at startup while <see cref="MainWindow"/> is constructed, which briefly needs
/// its own resources ready before it can appear - without this, the app looks like it failed to
/// launch during that window. The mod scan itself runs on a background thread
/// (MainViewModel.RescanModsForStartupAsync) rather than blocking construction, so this window
/// closes again almost immediately.
/// </summary>
public partial class SplashWindow
{
    public SplashWindow()
    {
        InitializeComponent();

        // Started here rather than via a XAML Loaded trigger: Loaded fires at a lower dispatcher
        // priority than App.xaml.cs's startup Yield waits for, so the trigger could still be pending
        // by the time the window is shown. Begin() takes effect immediately.
        ((Storyboard)Spinner.Resources["SpinnerStoryboard"]).Begin(Spinner);
    }
}
