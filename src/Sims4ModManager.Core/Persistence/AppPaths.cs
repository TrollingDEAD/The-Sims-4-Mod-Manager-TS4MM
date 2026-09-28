namespace Sims4ModManager.Core.Persistence;

/// <summary>Where the app keeps its own data (%AppData%\Sims4ModManager) - never inside the game folders.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sims4ModManager");

    public static string Cache => Path.Combine(Root, "cache");
}
