using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sims4ModManager.Core.Localization;
using Velopack;

namespace Sims4ModManager.App;

public partial class App : Application
{
    /// <summary>%AppData%\Sims4ModManager\error.log - unexpected errors end up here instead of vanishing silently.</summary>
    public static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sims4ModManager", "error.log");

    /// <summary>
    /// Replaces the WPF-generated Main(): Velopack needs first say on startup (it handles install/update/
    /// uninstall hooks and exits early for them) before any window is created. App.xaml is a Page, not an
    /// ApplicationDefinition (see the .csproj), so the SDK no longer generates its own Main().
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { Log(args.Exception); args.SetObserved(); };

        // Fallback for broken graphics drivers: S4MM_SOFTWARE_RENDERING=1 renders without the GPU.
        if (Environment.GetEnvironmentVariable("S4MM_SOFTWARE_RENDERING") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Language (restart required to switch): view models translate their texts, the translator the XAML ones.
        Core.Localization.L.Use(new Core.AppSettingsStore().Load().Language);
        System.Threading.Thread.CurrentThread.CurrentCulture = Core.Localization.L.Culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = Core.Localization.L.Culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(Core.Localization.L.Culture.IetfLanguageTag)));
        Services.UiTranslator.Enable();

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show(
            L.F("Ein unerwarteter Fehler ist aufgetreten:{0}{1}{2}{3}", Environment.NewLine, e.Exception.Message, Environment.NewLine, Environment.NewLine) +
            L.F("Details: {0}", ErrorLogPath),
            L.T("Sims 4 Mod Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    internal static void Log(Exception? exception)
    {
        if (exception is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:u}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing sensible left to do.
        }
    }
}
