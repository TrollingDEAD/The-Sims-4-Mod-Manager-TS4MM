using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sims4ModManager.App.Services;
using Sims4ModManager.Core;
using Sims4ModManager.Core.Diagnostics;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.ViewModels;

/// <summary>The "Diagnose" tab: game error logs with the suspected mods, and the 50/50 assistant.</summary>
public partial class DiagnoseViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly BisectSession _bisect = new();
    private int _version;

    public DiagnoseViewModel(MainViewModel main, IDialogService dialogs)
    {
        _main = main;
        _dialogs = dialogs;
        LoadBisect();
    }

    // --- Error logs ------------------------------------------------------------------------------

    public ObservableCollection<ErrorGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private ErrorGroupViewModel? selectedGroup;
    [ObservableProperty] private string logSummary = L.T("Fehlerprotokolle werden gelesen …");
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int newErrorCount;

    private string? GameDataFolder => _main.ModsPath is { } mods ? ModsFolderLocator.TryGetGameDataFolder(mods)?.Path : null;

    public async Task RefreshAsync()
    {
        int version = ++_version;
        string? data = GameDataFolder;
        if (data is null)
        {
            Groups.Clear();
            LogSummary = L.T("Kein Sims 4-Ordner gefunden.");
            return;
        }

        var mods = _main.CurrentMods;
        string? modsPath = _main.ModsPath;
        IsBusy = true;
        try
        {
            var entries = await Task.Run(() => ErrorLogAnalyzer.Analyze(data, modsPath, mods));
            if (version != _version)
                return;

            var groups = ErrorLogAnalyzer.Group(entries);
            string? selected = SelectedGroup?.Category;
            Groups.Clear();
            foreach (var group in groups)
                Groups.Add(new ErrorGroupViewModel(group));
            SelectedGroup = Groups.FirstOrDefault(g => g.Category == selected) ?? Groups.FirstOrDefault();

            int current = groups.Count(g => !g.Latest.IsFromOlderGameVersion);
            NewErrorCount = current;
            LogSummary = groups.Count == 0
                ? L.T("Keine Fehlerprotokolle – das Spiel hat keine Fehler gemeldet.")
                : L.F("{0} Fehler in {1} Gruppe(n)", entries.Count, groups.Count) +
                  (current == 0 ? L.T(" – alle aus älteren Spielversionen (vermutlich erledigt).") : L.F(" – {0} aus der aktuellen Spielversion.", current));
        }
        finally
        {
            if (version == _version)
                IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisableSuspectAsync(ErrorSuspect? suspect)
    {
        if (suspect is null || !await _main.EnsureGameClosedAsync())
            return;
        if (!await _dialogs.ConfirmAsync(L.T("Verdächtigen Mod deaktivieren"),
                L.F("„{0}“ deaktivieren?{1}{2}{3}{4}", suspect.Mod.DisplayName, Environment.NewLine, suspect.Reason, Environment.NewLine, Environment.NewLine) +
                L.T("Danach das Spiel testen. Tritt der Fehler nicht mehr auf, war das der Verursacher – ein Update des Mods beim Ersteller suchen."),
                L.T("Deaktivieren")))
            return;

        using (var recorder = _main.Journal.Begin(L.F("Verdächtigen Mod deaktiviert: „{0}“", suspect.Mod.DisplayName)))
            ModToggleService.SetEnabled(suspect.Mod, false, recorder);
        _main.AfterChange();
        _main.StatusMessage = L.F("„{0}“ deaktiviert. {1}", suspect.Mod.DisplayName, MainViewModel.UndoHint);
    }

    /// <summary>Moves logs written by older game versions into the backup (they only clutter the list).</summary>
    [RelayCommand]
    private async Task CleanupOldLogsAsync()
    {
        var old = Groups.SelectMany(g => g.Model.Entries).Where(e => e.IsFromOlderGameVersion)
            .Select(e => e.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => Groups.SelectMany(g => g.Model.Entries).Where(e => e.FilePath == f).All(e => e.IsFromOlderGameVersion))
            .ToList();
        if (old.Count == 0)
        {
            _main.StatusMessage = L.T("Keine alten Fehlerprotokolle vorhanden.");
            return;
        }
        if (!await _dialogs.ConfirmAsync(L.T("Alte Protokolle aufräumen"),
                L.F("{0} Protokolldatei(en) stammen aus älteren Spielversionen. Sie werden in die Sicherung verschoben (nicht gelöscht).", old.Count), L.T("Aufräumen")))
            return;

        using (var recorder = _main.Journal.Begin(L.F("{0} alte Fehlerprotokolle aufgeräumt", old.Count)))
            foreach (string file in old.Where(File.Exists))
                recorder.Delete(file);
        _main.History.Refresh(selectNewest: true);
        await RefreshAsync();
        _main.StatusMessage = L.F("{0} alte Protokolle aufgeräumt. {1}", old.Count, MainViewModel.UndoHint);
    }

    [RelayCommand]
    private void OpenLog()
    {
        string? file = SelectedGroup?.Model.Latest.FilePath;
        if (file is not null && File.Exists(file))
            Process.Start("explorer.exe", $"/select,\"{file}\"");
    }

    // --- 50/50 assistant -------------------------------------------------------------------------

    [ObservableProperty] private BisectState? bisect;
    [ObservableProperty] private string bisectProblem = string.Empty;

    public bool IsBisectIdle => Bisect is null;
    public bool IsBisectRunning => Bisect is { IsFinished: false };
    public bool IsBisectFinished => Bisect is { IsFinished: true };
    public string BisectProgress => Bisect is null ? string.Empty
        : Bisect.IsFinished
            ? L.F("Fertig nach {0} Runde(n).", Bisect.Round)
            : L.F("Runde {0} · {1} Verdächtige · noch etwa {2} Runde(n)", Bisect.Round, Bisect.Candidates.Count, Bisect.RemainingRounds);
    public string BisectInstruction => Bisect is null ? string.Empty
        : L.F("Die Hälfte der Verdächtigen ({0} Testeinheiten) ist jetzt deaktiviert. ", Bisect.DisabledForTest.Count) +
          L.F("Starte das Spiel, versuche den Fehler „{0}“ auszulösen und beende das Spiel wieder. Dann hier antworten.", Bisect.Description);
    public IReadOnlyList<string> BisectResult => Bisect is { IsFinished: true } ? BisectSession.SuspectFiles(Bisect) : Array.Empty<string>();
    public IReadOnlyList<string> BisectLog => Bisect?.Log.AsEnumerable().Reverse().ToList() ?? new List<string>();

    partial void OnBisectChanged(BisectState? value)
    {
        OnPropertyChanged(nameof(IsBisectIdle));
        OnPropertyChanged(nameof(IsBisectRunning));
        OnPropertyChanged(nameof(IsBisectFinished));
        OnPropertyChanged(nameof(BisectProgress));
        OnPropertyChanged(nameof(BisectInstruction));
        OnPropertyChanged(nameof(BisectResult));
        OnPropertyChanged(nameof(BisectLog));
    }

    private void LoadBisect() => Bisect = _bisect.Load();

    [RelayCommand]
    private async Task StartBisectAsync()
    {
        if (_main.ModsPath is null || !await _main.EnsureGameClosedAsync())
            return;
        string problem = BisectProblem.Trim().Length > 0 ? BisectProblem.Trim() : L.T("das Problem");
        int enabled = _main.CurrentMods.Sum(m => m.Files.Count(f => f.IsEnabled));
        if (!await _dialogs.ConfirmAsync(L.T("50/50-Test starten"),
                L.F("Der Test grenzt den Verursacher von „{0}“ unter {1} aktiven Dateien ein.{2}{3}", problem, enabled, Environment.NewLine, Environment.NewLine) +
                L.T("In jeder Runde deaktiviert das Tool die Hälfte der Verdächtigen. Du startest das Spiel, prüfst ob das Problem noch auftritt, und antwortest hier. ") +
                L.T("Nach etwa ") + Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(2, enabled)))) + L.T(" Runden steht der Verursacher fest. ") +
                L.T("Am Ende wird alles wieder aktiviert. Der Fortschritt bleibt auch nach einem Neustart erhalten."), L.T("Starten")))
            return;

        using (var recorder = _main.Journal.Begin(L.F("50/50-Test gestartet: {0}", problem)))
            Bisect = _bisect.Start(_main.ModsPath, _main.CurrentMods, problem, recorder);
        _main.AfterChange();
        _main.StatusMessage = L.T("50/50-Test läuft – jetzt das Spiel starten und testen.");
    }

    [RelayCommand]
    private Task AnswerStillThereAsync() => AnswerAsync(true);

    [RelayCommand]
    private Task AnswerGoneAsync() => AnswerAsync(false);

    private async Task AnswerAsync(bool stillThere)
    {
        if (Bisect is null || !await _main.EnsureGameClosedAsync())
            return;
        using (var recorder = _main.Journal.Begin(L.F("50/50-Test Runde {0}: Problem {1}", Bisect.Round, (stillThere ? L.T("noch da") : "weg"))))
            Bisect = _bisect.Answer(Bisect, stillThere, recorder);
        OnBisectChanged(Bisect);
        _main.AfterChange();
        _main.StatusMessage = Bisect.IsFinished
            ? L.T("50/50-Test abgeschlossen – Ergebnis im Tab „Diagnose“. Alle Mods sind wieder aktiv.")
            : L.T("Nächste Runde vorbereitet – Spiel erneut testen.");
    }

    [RelayCommand]
    private async Task StopBisectAsync()
    {
        if (Bisect is null)
            return;
        if (Bisect.IsFinished)
        {
            _bisect.Discard();
            Bisect = null;
            return;
        }
        if (!await _dialogs.ConfirmAsync(L.T("50/50-Test abbrechen"), L.T("Test abbrechen und alle vom Test deaktivierten Mods wieder aktivieren?"), L.T("Abbrechen"), L.T("Weiter testen")))
            return;
        using (var recorder = _main.Journal.Begin(L.T("50/50-Test abgebrochen")))
            _bisect.Stop(Bisect, recorder);
        Bisect = null;
        _main.AfterChange();
    }

    /// <summary>Disables the file(s) the test identified and ends the test.</summary>
    [RelayCommand]
    private async Task DisableCulpritAsync()
    {
        if (Bisect is not { IsFinished: true } state || _main.ModsPath is null)
            return;
        var files = BisectSession.SuspectFiles(state);
        using (var recorder = _main.Journal.Begin(L.T("Verursacher aus 50/50-Test deaktiviert")))
        {
            foreach (string relative in files)
            {
                string path = Path.Combine(_main.ModsPath, relative);
                if (File.Exists(path))
                    recorder.Move(path, path + ModFileNaming.DisabledSuffix);
            }
        }
        _bisect.Discard();
        Bisect = null;
        _main.AfterChange();
        _main.StatusMessage = L.F("Verursacher deaktiviert ({0} Datei(en)). {1}", files.Count, MainViewModel.UndoHint);
    }
}

public sealed class ErrorGroupViewModel
{
    public ErrorGroupViewModel(ErrorLogGroup group) => Model = group;

    public ErrorLogGroup Model { get; }
    public string Category => Model.Category;
    public int Count => Model.Count;
    public bool IsOld => Model.Latest.IsFromOlderGameVersion;
    public string Headline => Model.Latest.Headline;
    // For crashes the headline already is the explanation; the protocol box shows only the raw report.
    public string Details => Model.Kind == ErrorLogKind.Crash && Model.Latest.Details.IndexOf(ErrorLogAnalyzer.CrashSeparator, StringComparison.Ordinal) is var i and >= 0
        ? Model.Latest.Details[(i + ErrorLogAnalyzer.CrashSeparator.Length)..]
        : Model.Latest.Details;
    public IReadOnlyList<ErrorSuspect> Suspects => Model.Latest.Suspects;
    public bool HasSuspects => Suspects.Count > 0;

    public string KindLabel => Model.Kind switch
    {
        ErrorLogKind.ScriptException => L.T("Skriptfehler"),
        ErrorLogKind.UiException => L.T("Oberflächenfehler"),
        ErrorLogKind.Crash => L.T("Absturz"),
        _ => L.T("Mod-Bericht")
    };

    public string MetaLabel =>
        L.F("{0}× · zuletzt {1} · Spielversion {2}", Count, Model.Latest.CreatedLocal?.ToString("g") ?? "?", Model.Latest.GameVersion ?? "?") +
        (IsOld ? L.T(" (älter als installiert)") : "");

    /// <summary>What the player can do, depending on what the analysis found.</summary>
    public string Advice
    {
        get
        {
            var latest = Model.Latest;
            if (latest.Suspects.Count > 0)
                return L.T("Der Fehler lässt sich einem installierten Mod zuordnen (unten). Deaktivieren und testen, dann beim Ersteller nach einem Update schauen.");
            if (latest.MissingScripts.Count > 0)
                return L.F("Der Fehler stammt von „{0}“ – dieser Mod ist nicht mehr installiert. Der Fehler ist damit vermutlich erledigt.", string.Join("“, „", latest.MissingScripts));
            if (IsOld)
                return L.T("Der Fehler stammt aus einer älteren Spielversion. Tritt er nicht mehr auf, kann das Protokoll aufgeräumt werden.");
            return Model.Kind switch
            {
                ErrorLogKind.Crash => L.T("Abstürze lassen sich keinem Mod direkt zuordnen. Der 50/50-Test grenzt den Verursacher ein."),
                ErrorLogKind.UiException => L.T("Oberflächenfehler entstehen oft durch UI-Mods oder CC mit fehlerhaften Katalogdaten. Tritt er wiederholt auf, hilft der 50/50-Test."),
                _ => L.T("Kein Mod wird im Fehler genannt – der Fehler liegt im Spielcode, ausgelöst womöglich durch Tuning- oder CC-Mods. Der 50/50-Test grenzt ihn ein.")
            };
        }
    }
}
