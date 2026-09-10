using System.Diagnostics;
using System.IO;

namespace DLSSGManager;

/// <summary>
/// Opens folders, documents and game executables on behalf of the user.
///
/// Nothing here assembles a command line: the target path is passed either as the program image
/// itself or through <see cref="ProcessStartInfo.ArgumentList"/>, which hands the argument to the
/// child as a discrete vector entry. No string is ever parsed by a shell, so a path containing
/// quotes, spaces or percent signs cannot turn into additional arguments.
/// </summary>
public static class Opener
{
    private static readonly string ExplorerImage =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static ProcessStartInfo ForExplorer(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExplorerImage,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("/n");
        return psi;
    }

    /// <summary>Shows a folder in File Explorer.</summary>
    public static bool Folder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!Directory.Exists(path)) return false;

        try
        {
            using var p = Process.Start(ForExplorer(path));
            return p is not null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("打开目录失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>Opens a file with whatever application the user has associated with it.</summary>
    public static bool Document(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path)) return false;

        try
        {
            // The document itself is the program image; the shell resolves its association.
            using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return p is not null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("打开文件失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts the game. ShellExecute is required so the game's own manifest, DRM hooks and
    /// launcher requirements behave the same as a double-click in Explorer.
    /// </summary>
    public static bool Launch(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        if (!File.Exists(exePath)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            };
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("启动游戏失败: " + ex.Message);
            return false;
        }
    }
}
