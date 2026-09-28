using System.Windows.Media.Animation;

namespace Sims4ModManager.App;

/// <summary>
/// Shown immediately at startup while <see cref="MainWindow"/> is constructed - which does a full
/// mod scan synchronously and can take a while. Without this, the app looks like it failed to
/// launch during that time.
/// </summary>
public partial class SplashWindow
{
    public SplashWindow()
    {
        InitializeComponent();

        // Started here rather than via a XAML Loaded trigger: Loaded fires at a lower dispatcher
        // priority than App.xaml.cs's startup Yield waits for, so the trigger could still be pending
        // when the blocking work begins - the spinner would never move. Begin() takes effect
        // immediately, before the window is even shown.
        ((Storyboard)Spinner.Resources["SpinnerStoryboard"]).Begin(Spinner);
    }
}
