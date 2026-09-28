using Sims4ModManager.Core.Backup;
using Sims4ModManager.Core.Models;
using Sims4ModManager.Core.Persistence;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Diagnostics;

/// <summary>
/// Persisted state of a 50/50 test. Files are stored by their enabled path relative to the Mods
/// folder, so the session survives rescans, app restarts and reboots.
/// </summary>
public sealed class BisectState
{
    public required string ModsPath { get; init; }
    public required string Description { get; init; }
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Test units: files that are switched together (e.g. a script mod and its package).</summary>
    public required List<List<string>> Units { get; init; }

    /// <summary>Units that may still contain the culprit.</summary>
    public required List<int> Candidates { get; set; }

    /// <summary>Units disabled for the current test.</summary>
    public List<int> DisabledForTest { get; set; } = new();

    public int Round { get; set; }
    public List<string> Log { get; init; } = new();

    public bool IsFinished => Candidates.Count <= 1;

    /// <summary>Rounds still needed (log2 of the candidates).</summary>
    public int RemainingRounds => Candidates.Count <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(Candidates.Count));
}

/// <summary>
/// The 50/50 method from the community troubleshooting guides, automated where a tool can help:
/// disable half of the suspects, let the player test the game, keep the half that still shows the
/// problem, repeat. The tool does all the switching (journaled) and remembers the progress; only
/// the player can judge whether the problem still occurs.
/// </summary>
public sealed class BisectSession
{
    private readonly string _statePath;

    public BisectSession(string? statePath = null)
    {
        _statePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sims4ModManager", "bisect.json");
    }

    public BisectState? Load() => JsonFile.TryRead<BisectState>(_statePath);

    private void Save(BisectState state) => JsonFile.WriteAtomic(_statePath, state);

    public void Discard() => JsonFile.Delete(_statePath);

    /// <summary>
    /// Starts a test over all currently enabled files. Files of one small mod folder and files with
    /// the same name (script + package) stay together; big collection folders are split per file.
    /// </summary>
    public BisectState Start(string modsPath, IReadOnlyList<ModEntry> mods, string description, ChangeRecorder recorder)
    {
        var units = new List<List<string>>();
        foreach (var mod in mods)
        {
            var enabled = mod.Files.Where(f => f.IsEnabled).Select(f => Path.GetRelativePath(modsPath, f.AbsolutePath)).ToList();
            if (enabled.Count == 0)
                continue;
            if (enabled.Count <= 10)
            {
                units.Add(enabled);
                continue;
            }
            units.AddRange(enabled
                .GroupBy(p => Path.Combine(Path.GetDirectoryName(p) ?? "", Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.ToList()));
        }

        var state = new BisectState
        {
            ModsPath = modsPath,
            Description = description,
            Units = units,
            Candidates = Enumerable.Range(0, units.Count).ToList()
        };
        state.Log.Add(L.F("Start: {0} Testeinheiten ({1} Dateien).", units.Count, units.Sum(u => u.Count)));
        PrepareNextTest(state, recorder);
        return state;
    }

    /// <summary>
    /// Records the player's answer and prepares the next round (or finishes).
    /// <paramref name="problemStillThere"/> = the problem still occurred with the current half disabled.
    /// </summary>
    public BisectState Answer(BisectState state, bool problemStillThere, ChangeRecorder recorder)
    {
        var tested = state.DisabledForTest;
        state.Candidates = problemStillThere
            ? state.Candidates.Except(tested).ToList()   // culprit is among the enabled half
            : tested.ToList();                           // disabling this half fixed it
        state.Log.Add(L.F("Runde {0}: Problem {1} → {2} Verdächtige übrig.", state.Round, (problemStillThere ? L.T("noch da") : "weg"), state.Candidates.Count));

        if (state.IsFinished)
        {
            SetUnits(state, Enumerable.Range(0, state.Units.Count), enable: true, recorder);
            state.DisabledForTest = new List<int>();
            Save(state);
            return state;
        }

        PrepareNextTest(state, recorder);
        return state;
    }

    /// <summary>Ends the test and re-enables everything the test had disabled.</summary>
    public void Stop(BisectState state, ChangeRecorder recorder)
    {
        SetUnits(state, Enumerable.Range(0, state.Units.Count), enable: true, recorder);
        Discard();
    }

    /// <summary>Relative paths of the remaining suspects.</summary>
    public static IReadOnlyList<string> SuspectFiles(BisectState state) =>
        state.Candidates.SelectMany(i => state.Units[i]).ToList();

    private void PrepareNextTest(BisectState state, ChangeRecorder recorder)
    {
        state.Round++;
        var half = state.Candidates.Take((state.Candidates.Count + 1) / 2).ToList();

        // Everything except the tested half is enabled - including units already cleared.
        SetUnits(state, Enumerable.Range(0, state.Units.Count).Except(half), enable: true, recorder);
        SetUnits(state, half, enable: false, recorder);
        state.DisabledForTest = half;
        state.Log.Add(L.F("Runde {0}: {1} von {2} Verdächtigen deaktiviert.", state.Round, half.Count, state.Candidates.Count));
        Save(state);
    }

    private static void SetUnits(BisectState state, IEnumerable<int> units, bool enable, ChangeRecorder recorder)
    {
        foreach (int unit in units)
        {
            foreach (string relative in state.Units[unit])
            {
                string enabledPath = Path.Combine(state.ModsPath, relative);
                string disabledPath = enabledPath + ModFileNaming.DisabledSuffix;
                string from = enable ? disabledPath : enabledPath;
                string to = enable ? enabledPath : disabledPath;
                if (File.Exists(from) && !File.Exists(to))
                    recorder.Move(from, to);
            }
        }
    }
}
