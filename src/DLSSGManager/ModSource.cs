using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DLSSGManager;

/// <summary>
/// A folder holding the project's published files: root <c>version.dll</c> + <c>dlssg_sm86.ini</c>,
/// the four alternates under <c>altnative\</c>, and the two presets under <c>config\presets\</c>.
/// </summary>
public sealed class ModSource
{
    public static readonly string[] ProxyCandidates = { "version.dll", "winmm.dll", "dinput8.dll", "winhttp.dll", "dxgi.dll" };

    public const string IniName = "dlssg_sm86.ini";
    public const string LogDirName = "dlssg_sm86";

    public string Root { get; }
    public bool IsValid { get; }
    public string Version { get; } = Loc.T("ModSource.UnknownVersion");
    public List<string> Proxies { get; } = new();
    public string ValidationMessage { get; } = "";

    public ModSource(string root)
    {
        Root = root ?? "";
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            ValidationMessage = Loc.T("ModSource.DirMissing");
            return;
        }

        var iniPath = Path.Combine(root, IniName);
        if (!File.Exists(iniPath)) ValidationMessage = Loc.T("ModSource.IniMissing", IniName);

        foreach (var name in ProxyCandidates)
        {
            if (File.Exists(ResolveDllPath(root, name))) Proxies.Add(name);
        }

        if (Proxies.Count == 0)
            ValidationMessage = ValidationMessage.Length > 0
                ? Loc.T("ModSource.IniMissingAndNoDll", IniName)
                : Loc.T("ModSource.NoDll");

        IsValid = Proxies.Count > 0 && File.Exists(iniPath);
        if (IsValid) Version = ReadVersion(iniPath) ?? Loc.T("ModSource.UnknownVersion");
    }

    public static string ResolveDllPath(string root, string proxyName) =>
        string.Equals(proxyName, "version.dll", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, "version.dll")
            : Path.Combine(root, "altnative", proxyName);

    public string DllPath(string proxyName) => ResolveDllPath(Root, proxyName);

    public string IniPath => Path.Combine(Root, IniName);

    public string IniText
    {
        get
        {
            try { return File.ReadAllText(IniPath, Encoding.UTF8); }
            catch { return ""; }
        }
    }

    public string? PresetPath(string preset) => new[]
    {
        Path.Combine(Root, "config", "presets", preset),
        Path.Combine(Root, "config", "presets", preset + ".ini"),
        Path.Combine(Root, preset),
    }.FirstOrDefault(File.Exists);

    /// <summary>Reads the "; Native 0.2.3." banner the shipped INI opens with.</summary>
    public static string? ReadVersion(string iniPath)
    {
        try
        {
            foreach (var line in File.ReadLines(iniPath).Take(6))
            {
                var m = Regex.Match(line, @"Native\s+([0-9]+(?:\.[0-9]+)+)");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch
        {
            // A version banner is cosmetic; an unreadable INI is reported by IsValid instead.
        }

        return null;
    }
}
