using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DLSSGManager;

/// <summary>
/// A folder holding the project's published files: root <c>version.dll</c> + <c>dlssg_sm86.ini</c>,
/// the four alternates under <c>altnative\</c>, and the two presets under <c>config\presets\</c>.
/// Proxies the user adds themselves also live under <c>altnative\</c>, beside the published ones.
/// </summary>
public sealed class ModSource
{
    public static readonly string[] ProxyCandidates = { "version.dll", "winmm.dll", "dinput8.dll", "winhttp.dll", "dxgi.dll" };

    /// <summary>
    /// Every name a proxy can legitimately occupy: the five above plus <c>d3d12.dll</c>, which community
    /// builds use on games whose protection module claims the classic names first (the game itself loads
    /// d3d12.dll later, on demand).
    ///
    /// This is the *scanning* set — cleanup, quarantine detection and the multiple-proxy check — and it
    /// is what decides whether a file the user adds is a plausible entry name. Deployment picks from
    /// <see cref="AvailableProxies"/> instead: the project's own names plus whatever has actually been
    /// added, so a name is only deployable when a DLL for it exists.
    /// </summary>
    public static readonly string[] KnownProxyNames =
        ProxyCandidates.Concat(new[] { "d3d12.dll" }).ToArray();

    /// <summary>True when a file name is one a proxy of this kind can take; the check is case-insensitive.</summary>
    public static bool IsKnownProxyName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        KnownProxyNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    public const string IniName = "dlssg_sm86.ini";
    public const string LogDirName = "dlssg_sm86";

    /// <summary>Folder holding the entry points other than version.dll, published and imported alike.</summary>
    public const string AltDirName = "altnative";

    public string Root { get; }
    public bool IsValid { get; }
    public string Version { get; } = Loc.T("ModSource.UnknownVersion");
    public List<string> Proxies { get; } = new();
    public string ValidationMessage { get; } = "";

    /// <summary>Proxy DLLs in <c>altnative\</c> that this project does not ship — added by the user.</summary>
    public IReadOnlyList<string> ImportedProxies { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Entry names this source can deploy: the project's own five first, then imported ones. Auto-pick
    /// walks this order, so a game only lands on an imported entry when the classic names are taken.
    /// </summary>
    public IReadOnlyList<string> AvailableProxies { get; private set; } = ProxyCandidates;

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

        ImportedProxies = ImportedProxyNames(root);
        AvailableProxies = ProxyCandidates.Concat(ImportedProxies).ToArray();

        if (Proxies.Count == 0 && ImportedProxies.Count == 0)
            ValidationMessage = ValidationMessage.Length > 0
                ? Loc.T("ModSource.IniMissingAndNoDll", IniName)
                : Loc.T("ModSource.NoDll");

        IsValid = (Proxies.Count > 0 || ImportedProxies.Count > 0) && File.Exists(iniPath);
        if (IsValid) Version = ReadVersion(iniPath) ?? Loc.T("ModSource.UnknownVersion");
    }

    public static string ResolveDllPath(string root, string proxyName) =>
        string.Equals(proxyName, "version.dll", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, "version.dll")
            : Path.Combine(root, AltDirName, proxyName);

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

    /// <summary>
    /// Proxy DLLs under <c>altnative\</c> beyond the five the project ships, i.e. files the user added.
    /// The file name is the entry name — it is the DLL name the game resolves — so it is the whole
    /// contract, and nothing else about the file is assumed here.
    /// </summary>
    private static List<string> ImportedProxyNames(string root)
    {
        var result = new List<string>();

        try
        {
            var dir = Path.Combine(root, AltDirName);
            if (!Directory.Exists(dir)) return result;

            foreach (var path in Directory.EnumerateFiles(dir, "*.dll"))
            {
                var name = Path.GetFileName(path);
                if (ProxyCandidates.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                result.Add(name);
            }
        }
        catch
        {
            // An unreadable folder simply yields no imported entries.
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// Adds a proxy DLL the user supplies — a community build such as <c>d3d12.dll</c>, which this
    /// project does not ship — so it can be deployed like the bundled entry points.
    ///
    /// The file is copied into <c>altnative\</c> under its own name, because that name *is* the entry
    /// name: it is the DLL name the game resolves. That is also why it cannot be one of the project's
    /// own names — an import would shadow a build whose signature every ownership check relies on.
    ///
    /// Nothing about the file is verified: the manager did not download it and cannot vouch for it. What
    /// it can do is copy it faithfully, record its hash when deploying, and remove exactly what it wrote.
    /// </summary>
    public static OpResult ImportProxy(string root, string sourceFile)
    {
        var r = new OpResult();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            r.Fail(Loc.T("Proxy.AddNeedSource"));
            return r;
        }

        var name = Path.GetFileName(sourceFile ?? "");
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            r.Fail(Loc.T("Proxy.AddNeedDll"));
            return r;
        }

        if (ProxyCandidates.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            r.Fail(Loc.T("Proxy.AddReserved", name));
            return r;
        }

        if (!File.Exists(sourceFile))
        {
            r.Fail(Loc.T("Proxy.AddFileMissing", sourceFile ?? ""));
            return r;
        }

        try
        {
            var dest = ResolveDllPath(root, name);
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var replaced = File.Exists(dest);
            var tmp = dest + ".dlssgtmp";
            File.Copy(sourceFile, tmp, overwrite: true);
            File.Move(tmp, dest, overwrite: true);
            DeploymentService.Unblock(dest);

            if (replaced) r.Note(Loc.T("Proxy.AddReplaced", name));
            r.Note(Loc.T("Proxy.AddedFile", name, new FileInfo(dest).Length / 1024));

            // Say what the file is, since nothing else about it is known: whether it carries an intact
            // signature is the one fact the manager can establish on its own.
            using (var cert = DeploymentService.ReadSignerCertificate(dest, out var signatureIntact))
            {
                r.Note(signatureIntact && cert is not null
                    ? Loc.T("Proxy.AddedSigner", cert.Subject ?? "")
                    : Loc.T("Proxy.AddedUnsigned"));
            }

            r.Message = Loc.T("Proxy.Added", name, root);
        }
        catch (Exception ex)
        {
            r.Fail(Loc.T("Proxy.AddFailed", ex.Message));
        }

        return r;
    }
}
