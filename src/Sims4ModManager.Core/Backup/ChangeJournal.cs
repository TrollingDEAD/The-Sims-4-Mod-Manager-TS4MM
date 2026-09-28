using Sims4ModManager.Core.Persistence;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.Core.Backup;

public enum ChangeKind
{
    /// <summary>File moved or renamed (also enable/disable): undo moves it back.</summary>
    Moved,
    /// <summary>File content replaced; the original is kept as a backup copy.</summary>
    Modified,
    /// <summary>New file; undo removes it (into the change set's folder, never a hard delete).</summary>
    Created,
    /// <summary>File removed; it lives on in the change set's folder until undone.</summary>
    Deleted,
    /// <summary>Empty folder removed; undo recreates it.</summary>
    DirectoryDeleted,
    /// <summary>Folder created (e.g. by sorting); undo removes it again if it is empty.</summary>
    DirectoryCreated
}

/// <summary>One recorded file operation. <see cref="BackupFile"/> is relative to the change set folder.</summary>
public sealed record ChangeOperation(ChangeKind Kind, string Path, string? OriginalPath = null, string? BackupFile = null);

public sealed class ChangeSet
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public List<ChangeOperation> Operations { get; init; } = new();
    public bool Completed { get; set; }
    public DateTime? UndoneUtc { get; set; }

    public bool IsUndone => UndoneUtc is not null;
}

public sealed record UndoResult(int Restored, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Backup system for every action that changes files in the Mods or Tray folder. Each action is a
/// change set: a folder under the journal root holding a manifest of the file operations plus
/// copies of everything that was overwritten or removed. Any change set can be undone later.
/// The manifest is written while the action runs, so even an interrupted action can be undone.
/// </summary>
public sealed class ChangeJournal
{
    public const int DefaultMaxChangeSets = 100;
    private const string ManifestName = "manifest.json";

    public ChangeJournal(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sims4ModManager", "backups", "journal");
    }

    public string RootDirectory { get; }

    /// <summary>Starts recording a new change set. Dispose the recorder when the action is done.</summary>
    public ChangeRecorder Begin(string description)
    {
        string id = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{Guid.NewGuid().ToString("N")[..6]}";
        var set = new ChangeSet { Id = id, Description = description, CreatedUtc = DateTime.UtcNow };
        return new ChangeRecorder(this, set, Path.Combine(RootDirectory, id));
    }

    /// <summary>All change sets, newest first.</summary>
    public IReadOnlyList<ChangeSet> List()
    {
        if (!Directory.Exists(RootDirectory))
            return Array.Empty<ChangeSet>();

        return Directory.GetDirectories(RootDirectory)
            .Select(dir => JsonFile.TryRead<ChangeSet>(Path.Combine(dir, ManifestName)))
            .OfType<ChangeSet>()
            .OrderByDescending(s => s.CreatedUtc)
            .ToList();
    }

    public string GetDirectory(ChangeSet set) => Path.Combine(RootDirectory, set.Id);

    /// <summary>
    /// Reverts a change set, newest operation first. Operations whose target has changed in the
    /// meantime (e.g. the file was moved again) are skipped and reported instead of overwriting data.
    /// </summary>
    public UndoResult Undo(string id)
    {
        string dir = Path.Combine(RootDirectory, id);
        var set = JsonFile.TryRead<ChangeSet>(Path.Combine(dir, ManifestName))
                  ?? throw new InvalidOperationException($"Sicherungspunkt {id} nicht gefunden.");
        if (set.IsUndone)
            return new UndoResult(0, new[] { L.T("Dieser Sicherungspunkt wurde bereits rückgängig gemacht.") });

        var errors = new List<string>();
        int restored = 0;
        int n = 0;

        foreach (var op in Enumerable.Reverse(set.Operations))
        {
            n++;
            try
            {
                string? error = UndoOperation(op, dir, n);
                if (error is null)
                    restored++;
                else
                    errors.Add(error);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{System.IO.Path.GetFileName(op.Path)}: {ex.Message}");
            }
        }

        set.UndoneUtc = DateTime.UtcNow;
        JsonFile.WriteAtomic(Path.Combine(dir, ManifestName), set);
        return new UndoResult(restored, errors);
    }

    private static string? UndoOperation(ChangeOperation op, string setDir, int n)
    {
        switch (op.Kind)
        {
            case ChangeKind.Moved:
                if (!File.Exists(op.Path))
                    return L.F("{0}: Datei existiert nicht mehr an {1}.", Path.GetFileName(op.Path), op.Path);
                if (File.Exists(op.OriginalPath))
                    return L.F("{0}: Am ursprünglichen Ort liegt bereits eine Datei.", Path.GetFileName(op.OriginalPath));
                Directory.CreateDirectory(Path.GetDirectoryName(op.OriginalPath!)!);
                File.Move(op.Path, op.OriginalPath!);
                return null;

            case ChangeKind.Modified:
            {
                string backup = Path.Combine(setDir, op.BackupFile!);
                if (!File.Exists(backup))
                    return L.F("{0}: Sicherungskopie fehlt.", Path.GetFileName(op.Path));
                if (File.Exists(op.Path))
                    File.Copy(op.Path, Path.Combine(setDir, $"undo-{n}_{Path.GetFileName(op.Path)}"), overwrite: true);
                Directory.CreateDirectory(Path.GetDirectoryName(op.Path)!);
                File.Copy(backup, op.Path, overwrite: true);
                return null;
            }

            case ChangeKind.Created:
                if (File.Exists(op.Path))
                    File.Move(op.Path, Path.Combine(setDir, $"undo-{n}_{Path.GetFileName(op.Path)}"), overwrite: true);
                return null;

            case ChangeKind.DirectoryDeleted:
                Directory.CreateDirectory(op.Path);
                return null;

            case ChangeKind.DirectoryCreated:
                if (Directory.Exists(op.Path) && !Directory.EnumerateFileSystemEntries(op.Path).Any())
                    Directory.Delete(op.Path);
                return null;

            case ChangeKind.Deleted:
            {
                string backup = Path.Combine(setDir, op.BackupFile!);
                if (!File.Exists(backup))
                    return L.F("{0}: Sicherungskopie fehlt.", Path.GetFileName(op.OriginalPath));
                if (File.Exists(op.OriginalPath))
                    return L.F("{0}: Am ursprünglichen Ort liegt bereits eine Datei.", Path.GetFileName(op.OriginalPath));
                Directory.CreateDirectory(Path.GetDirectoryName(op.OriginalPath!)!);
                File.Move(backup, op.OriginalPath!);
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>Deletes the oldest change sets beyond <paramref name="keep"/>.</summary>
    public int Prune(int keep = DefaultMaxChangeSets)
    {
        int removed = 0;
        foreach (var set in List().Skip(keep))
        {
            try
            {
                Directory.Delete(GetDirectory(set), recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked backup file - try again next time.
            }
        }
        return removed;
    }

    internal static void Save(string dir, ChangeSet set)
    {
        Directory.CreateDirectory(dir);
        JsonFile.WriteAtomic(Path.Combine(dir, ManifestName), set);
    }
}

/// <summary>
/// Performs file operations and records them in a change set. Every mod-changing code path goes
/// through this class, so nothing is overwritten or removed without a way back.
/// </summary>
public sealed class ChangeRecorder : IDisposable
{
    private const int SaveEvery = 50; // bulk actions (e.g. disabling 2000 mods) shouldn't flush the manifest per file

    private readonly ChangeJournal _journal;
    private readonly ChangeSet _set;
    private readonly string _dir;
    private int _unsaved;
    private bool _disposed;

    internal ChangeRecorder(ChangeJournal journal, ChangeSet set, string dir)
    {
        _journal = journal;
        _set = set;
        _dir = dir;
    }

    public string Id => _set.Id;
    public string Directory => _dir;
    public int OperationCount => _set.Operations.Count;

    /// <summary>Moves/renames a file (creating the target folder if needed).</summary>
    public void Move(string from, string to)
    {
        CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
        Record(new ChangeOperation(ChangeKind.Moved, to, OriginalPath: from));
    }

    /// <summary>Replaces a file's content: the original is backed up, then <paramref name="writeTo"/> writes the new content to a temp path that atomically replaces the file.</summary>
    public void Replace(string path, Action<string> writeTo)
    {
        string backupName = BackupCopy(path);
        string temp = path + ".s4mm-tmp";
        try
        {
            writeTo(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        Record(new ChangeOperation(ChangeKind.Modified, path, BackupFile: backupName));
    }

    /// <summary>Copies <paramref name="source"/> to <paramref name="target"/>; an existing target is backed up first.</summary>
    public void CopyIn(string source, string target)
    {
        CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            Replace(target, temp => File.Copy(source, temp, overwrite: true));
            return;
        }

        string temp = target + ".s4mm-tmp";
        File.Copy(source, temp, overwrite: true);
        File.Move(temp, target);
        Record(new ChangeOperation(ChangeKind.Created, target));
    }

    /// <summary>Creates a new file: <paramref name="writeTo"/> writes it to a temp path that is then moved into place. Undo removes it.</summary>
    public void Create(string target, Action<string> writeTo)
    {
        if (File.Exists(target))
            throw new IOException(L.F("{0} existiert bereits.", Path.GetFileName(target)));
        CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + ".s4mm-tmp";
        try
        {
            writeTo(temp);
            File.Move(temp, target);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        Record(new ChangeOperation(ChangeKind.Created, target));
    }

    /// <summary>Creates a folder (and missing parents), recording each new one so undo can remove them again.</summary>
    public void CreateDirectory(string path)
    {
        var missing = new Stack<string>();
        for (string? dir = Path.GetFullPath(path); dir is not null && !System.IO.Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
            missing.Push(dir);
        foreach (string dir in missing)
        {
            System.IO.Directory.CreateDirectory(dir);
            Record(new ChangeOperation(ChangeKind.DirectoryCreated, dir));
        }
    }

    /// <summary>Removes an empty folder (does nothing if it is not empty anymore).</summary>
    public void DeleteEmptyDirectory(string path)
    {
        if (!System.IO.Directory.Exists(path) || System.IO.Directory.EnumerateFileSystemEntries(path).Any())
            return;
        System.IO.Directory.Delete(path);
        Record(new ChangeOperation(ChangeKind.DirectoryDeleted, path));
    }

    /// <summary>Removes a file by moving it into the change set folder.</summary>
    public void Delete(string path)
    {
        string name = NextBackupName(path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir, name))!);
        File.Move(path, Path.Combine(_dir, name));
        Record(new ChangeOperation(ChangeKind.Deleted, path, OriginalPath: path, BackupFile: name));
    }

    private string BackupCopy(string path)
    {
        string name = NextBackupName(path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir, name))!);
        File.Copy(path, Path.Combine(_dir, name));
        return name;
    }

    private string NextBackupName(string path) =>
        Path.Combine("files", $"{_set.Operations.Count + 1:D5}", Path.GetFileName(path)); // original name kept: a backup folder can be re-installed like a download

    private void Record(ChangeOperation op)
    {
        _set.Operations.Add(op);
        if (_set.Operations.Count == 1 || ++_unsaved >= SaveEvery)
        {
            ChangeJournal.Save(_dir, _set);
            _unsaved = 0;
        }
    }

    /// <summary>Finalizes the change set. A set without operations leaves no trace.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_set.Operations.Count == 0)
        {
            if (System.IO.Directory.Exists(_dir))
                System.IO.Directory.Delete(_dir, recursive: true);
            return;
        }

        _set.Completed = true;
        ChangeJournal.Save(_dir, _set);
        _journal.Prune();
    }
}
