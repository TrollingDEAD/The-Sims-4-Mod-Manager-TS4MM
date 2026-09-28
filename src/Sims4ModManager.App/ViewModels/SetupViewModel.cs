using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>
/// First-start assistant: language, Mods folder (incl. OneDrive warning), the game's mod switches,
/// a first backup and an overview of the health check - each step reuses the regular tabs' logic.
/// </summary>
public partial class SetupViewModel : ObservableObject
{
    public SetupViewModel(MainViewModel main)
    {
        Main = main;
        isEnglish = !L.IsGerman;
    }

    public MainViewModel Main { get; }
    public HealthViewModel Health => Main.Health;
    public SavesViewModel Saves => Main.Saves;

    [ObservableProperty] private bool isEnglish;

    public bool LanguageChanged => IsEnglish == L.IsGerman;

    partial void OnIsEnglishChanged(bool value) => OnPropertyChanged(nameof(LanguageChanged));

    public string FolderHint => Main.ModsPath switch
    {
        null => L.T("Kein Mods-Ordner gefunden. Bitte das Spiel einmal starten oder den Ordner manuell wählen."),
        { } path when path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) =>
            L.T("Achtung: Der Ordner liegt in OneDrive. Die Synchronisierung kann Mods sperren oder nur in der Cloud ablegen – am besten „Dokumente“ von der OneDrive-Sicherung ausnehmen."),
        _ => L.T("Ordner gefunden.")
    };

    public bool FolderOk => Main.ModsPath is not null && !Main.ModsPath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);

    /// <summary>Asks the window to close.</summary>
    public event Action? CloseRequested;

    public void RefreshFolder()
    {
        OnPropertyChanged(nameof(FolderHint));
        OnPropertyChanged(nameof(FolderOk));
    }

    [RelayCommand]
    private void DetectFolder()
    {
        Main.DetectModsFolderCommand.Execute(null);
        RefreshFolder();
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        string? folder = Main.Dialogs.PickFolder(L.T("Mods-Ordner auswählen"));
        if (folder is null)
            return;
        Main.SetModsPath(folder);
        RefreshFolder();
    }

    [RelayCommand]
    private void Finish()
    {
        Main.Settings.TryUpdate(s =>
        {
            s.SetupCompleted = true;
            s.Language = IsEnglish ? L.English : L.German;
        });
        CloseRequested?.Invoke();
        if (LanguageChanged)
        {
            if (Environment.ProcessPath is { } exe)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
    }

    [RelayCommand]
    private void ShowOverview()
    {
        Main.SelectedTabIndex = MainViewModel.TabOverview;
        FinishCommand.Execute(null);
    }
}
