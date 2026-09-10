using System.IO;

namespace DLSSGManager;

/// <summary>
/// Works out where the mod files live.
///
/// Two situations have to work: a shipped folder where <c>mod\</c> sits beside the executable, and a
/// source checkout run via <c>dotnet run</c>, where the executable is buried in
/// <c>bin\Debug\net8.0-windows\</c> and <c>mod\</c> belongs at the repository root several levels up.
/// Without the upward search a fresh clone would look in (and download into) its own bin folder.
/// </summary>
public static class ModSourceLocator
{
    /// <summary>How far above the executable to look for a repository checkout.</summary>
    private const int MaxLevelsUp = 6;

    /// <summary>A folder counts as a mod source only if it holds the INI the project ships.</summary>
    private const string MarkerFile = ModSource.IniName;

    /// <summary>Files that mark a repository root. The project ships no .sln, so several are checked.</summary>
    private static readonly string[] RepoMarkers = { ".git", ".gitignore", "*.sln", "*.slnx" };

    /// <summary>
    /// The folder to read mod files from, or null when none exists yet. A user-configured path wins;
    /// otherwise the folder beside the executable; otherwise the nearest ancestor that looks like a
    /// checkout holding <c>mod\</c>.
    /// </summary>
    public static string? FindExisting(string? configuredPath)
    {
        if (LooksLikeSource(configuredPath)) return configuredPath;

        var beside = AppPaths.BundledModDir;
        if (LooksLikeSource(beside)) return beside;

        var repo = FindRepositoryRoot();
        if (repo is not null)
        {
            var candidate = Path.Combine(repo, "mod");
            if (LooksLikeSource(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Where downloads should be written. Reuses an existing source so neither a checkout nor a
    /// shipped build ends up with a stray second copy; otherwise targets <c>mod\</c> at the repository
    /// root when running from source, or beside the executable when shipped.
    /// </summary>
    public static string ResolveTarget(string? configuredPath)
    {
        if (LooksLikeSource(configuredPath)) return configuredPath!;
        if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath)) return configuredPath!;

        var existing = FindExisting(null);
        if (existing is not null) return existing;

        var repo = FindRepositoryRoot();
        if (repo is not null) return Path.Combine(repo, "mod");

        return AppPaths.BundledModDir;
    }

    /// <summary>
    /// Walks up from the executable looking for a repository root, stopping before the drive root so
    /// we never treat an unrelated parent folder as one.
    /// </summary>
    public static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (var level = 0; level < MaxLevelsUp && dir is not null; level++)
        {
            if (IsRepositoryRoot(dir.FullName)) return dir.FullName;

            var parent = dir.Parent;
            if (parent is null || parent.FullName == dir.FullName) break;   // Reached the drive root.
            dir = parent;
        }

        return null;
    }

    private static bool IsRepositoryRoot(string path)
    {
        foreach (var marker in RepoMarkers)
        {
            try
            {
                if (marker.Contains('*'))
                {
                    if (Directory.EnumerateFiles(path, marker).Any()) return true;
                }
                else if (File.Exists(Path.Combine(path, marker)) || Directory.Exists(Path.Combine(path, marker)))
                {
                    return true;
                }
            }
            catch
            {
                // An unreadable folder is simply not a repository root.
            }
        }

        return false;
    }

    /// <summary>True when the folder exists and contains the file that identifies a mod source.</summary>
    public static bool LooksLikeSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        return File.Exists(Path.Combine(path, MarkerFile));
    }
}
