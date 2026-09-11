using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DLSSGManager;

/// <summary>Detected GPU. Identity is taken from the hardware id where available.</summary>
public sealed record GpuInfo(string Name, string Driver, string Router, string Advice)
{
    /// <summary>PCI device id, e.g. "2208". Null when it could not be read.</summary>
    public string? PciDeviceId { get; init; }

    /// <summary>Architecture derived from the hardware id, e.g. "Ampere".</summary>
    public string? HardwareFamily { get; init; }

    /// <summary>The product name claims a different architecture than the hardware id.</summary>
    public bool NameMismatchesHardware { get; init; }
}

/// <summary>An active display adapter, with the id Windows reports for the physical device.</summary>
public sealed record DisplayAdapter(string Name, string DeviceInstancePath, string? DeviceId);

/// <summary>
/// Display adapter probing through the Win32 display API and the driver registry key. No child
/// process is started for this; it only reads what Windows already knows.
///
/// The architecture is decided from the PCI device id rather than the product name. A name can be
/// edited in the registry (and on this project's development machine, it had been), while the
/// device id is bound to the physical part. Getting this wrong matters: the SM75/SM86 route and the
/// "this GPU does not need the mod" advice both depend on the architecture.
/// </summary>
public static class Gpu
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);

    private const int AttachedToDesktop = 0x00000001;

    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>Adapters currently attached to the desktop, with their hardware ids.</summary>
    public static List<DisplayAdapter> Adapters()
    {
        var result = new List<DisplayAdapter>();
        var dd = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };

        for (uint i = 0; EnumDisplayDevicesW(null, i, ref dd, 0); i++)
        {
            dd.cb = Marshal.SizeOf<DisplayDevice>();
            if ((dd.StateFlags & AttachedToDesktop) == 0) continue;

            var name = dd.DeviceString ?? "";
            if (name.Length == 0) continue;
            if (name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

            var path = dd.DeviceID ?? "";
            result.Add(new DisplayAdapter(name, path, ParsePciDeviceId(path)));
        }

        return result;
    }

    public static List<string> AdapterNames() => Adapters().Select(a => a.Name).ToList();

    /// <summary>
    /// Extracts the device id from a device instance path such as
    /// <c>PCI\VEN_10DE&amp;DEV_2208&amp;SUBSYS_...</c>. Null when the path carries no id.
    /// </summary>
    public static string? ParsePciDeviceId(string? deviceInstancePath)
    {
        var m = Regex.Match(deviceInstancePath ?? "", @"DEV_([0-9A-Fa-f]{4})");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>True when the device path identifies an NVIDIA adapter.</summary>
    public static bool IsNvidiaDevice(string? deviceInstancePath) =>
        (deviceInstancePath ?? "").Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Architecture from the PCI device id.
    ///
    /// NVIDIA assigns ids in contiguous blocks per architecture, so a range test distinguishes
    /// Turing from Ampere from Ada — which is all the route decision needs. Ids outside the known
    /// blocks return null rather than a guess.
    /// </summary>
    public static string? FamilyFromDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        if (!ushort.TryParse(deviceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return null;

        return id switch
        {
            >= 0x1E00 and <= 0x1FFF => "Turing",     // TU10x / TU11x
            >= 0x2180 and <= 0x21FF => "Turing",     // TU117
            >= 0x2200 and <= 0x22FF => "Ampere",     // GA102
            >= 0x2480 and <= 0x25FF => "Ampere",     // GA104 / GA106 / GA107
            >= 0x2680 and <= 0x28FF => "Ada",        // AD102 … AD107
            >= 0x2B80 and <= 0x2CFF => "Blackwell",
            _ => null,
        };
    }

    /// <summary>
    /// Architecture implied by a product name. Used only to cross-check the hardware id, never as
    /// the deciding signal.
    /// </summary>
    public static string? FamilyFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (Regex.IsMatch(name, @"RTX\s*50\d0")) return "Blackwell";
        if (Regex.IsMatch(name, @"RTX\s*40\d0")) return "Ada";
        if (Regex.IsMatch(name, @"RTX\s*30\d0")) return "Ampere";
        if (Regex.IsMatch(name, @"RTX\s*20\d0")) return "Turing";
        return null;
    }

    /// <summary>
    /// Resolves the architecture to act on, preferring the hardware id, and reports whether the
    /// product name disagrees with it.
    /// </summary>
    public static (string? Family, bool NameMismatch) Classify(string? name, string? deviceId)
    {
        var byHardware = FamilyFromDeviceId(deviceId);
        var byName = FamilyFromName(name);

        var mismatch = byHardware is not null
                       && byName is not null
                       && !string.Equals(byHardware, byName, StringComparison.Ordinal);

        return (byHardware ?? byName, mismatch);
    }

    /// <summary>The mod route for an architecture. Turing is the only family needing the older one.</summary>
    public static string RouteForFamily(string? family) =>
        string.Equals(family, "Turing", StringComparison.Ordinal) ? "SM75" : "SM86";

    public static string RouteForAdapter(string adapterName) => RouteForFamily(FamilyFromName(adapterName));

    /// <summary>
    /// Driver version for an adapter, read from the display class registry key. Matched on the
    /// hardware id first: the product name in that key can be edited, and on a machine where it has
    /// been, a name match would silently return nothing.
    /// </summary>
    public static string DriverVersion(string adapterName, string? deviceId = null)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (key is null) return "";

            var subs = key.GetSubKeyNames()
                .Where(s => s.Length == 4 && s.All(char.IsDigit))
                .ToList();

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                foreach (var sub in subs)
                {
                    using var dev = key.OpenSubKey(sub);
                    var match = dev?.GetValue("MatchingDeviceId") as string ?? "";
                    if (match.Contains($"DEV_{deviceId}", StringComparison.OrdinalIgnoreCase))
                        return dev?.GetValue("DriverVersion") as string ?? "";
                }
            }

            foreach (var sub in subs)
            {
                using var dev = key.OpenSubKey(sub);
                var desc = dev?.GetValue("DriverDesc") as string ?? "";
                if (desc.Length > 0 && desc.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
                    return dev?.GetValue("DriverVersion") as string ?? "";
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("读取驱动版本失败: " + ex.Message);
        }

        return "";
    }

    /// <summary>
    /// Whether Windows Hardware-Accelerated GPU Scheduling is on.
    ///
    /// <c>HwSchMode</c>: 1 = off, 2 = on. A missing value means the platform or driver does not
    /// expose the setting at all, which is reported as null so the caller can stay quiet instead of
    /// claiming it is off.
    /// </summary>
    public static bool? HardwareSchedulingEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            var raw = key?.GetValue("HwSchMode");
            if (raw is null) return null;

            return Convert.ToInt32(raw) == 2;
        }
        catch (Exception ex)
        {
            AppPaths.Log("读取硬件加速 GPU 计划状态失败: " + ex.Message);
            return null;
        }
    }

    /// <summary>Advice built from a name alone, used by tests and as a fallback.</summary>
    public static string AdviceForAdapter(string adapterName, string driver) =>
        BuildAdvice(adapterName, driver, FamilyFromName(adapterName), false, null);

    /// <summary>
    /// Rebuilds the advice text for an already-probed adapter.
    ///
    /// The text is produced from code, so it does not follow the interface language on its own.
    /// Re-rendering after a language change needs the same inputs the probe had, which the record
    /// carries.
    /// </summary>
    public static string AdviceFor(GpuInfo info) =>
        BuildAdvice(
            info.Name,
            info.Driver,
            info.HardwareFamily ?? FamilyFromName(info.Name),
            info.NameMismatchesHardware,
            info.PciDeviceId);

    public static GpuInfo Probe()
    {
        var adapters = Adapters();

        // Prefer the vendor id: a renamed adapter would not necessarily keep "NVIDIA" in its name.
        var nvidia = adapters.FirstOrDefault(a => a.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                     ?? adapters.FirstOrDefault(a => IsNvidiaDevice(a.DeviceInstancePath));

        if (nvidia is null)
        {
            return new GpuInfo(
                adapters.Count > 0 ? string.Join(" / ", adapters.Select(a => a.Name)) : Loc.T("Gpu.NotFound"),
                "",
                "SM86",
                adapters.Count == 0
                    ? Loc.T("Gpu.NoAdapter")
                    : Loc.T("Gpu.NotFoundAdvice"));
        }

        var driver = DriverVersion(nvidia.Name, nvidia.DeviceId);
        var (family, mismatch) = Classify(nvidia.Name, nvidia.DeviceId);

        return new GpuInfo(
            nvidia.Name,
            driver,
            RouteForFamily(family),
            BuildAdvice(nvidia.Name, driver, family, mismatch, nvidia.DeviceId))
        {
            PciDeviceId = nvidia.DeviceId,
            HardwareFamily = FamilyFromDeviceId(nvidia.DeviceId),
            NameMismatchesHardware = mismatch,
        };
    }

    private static string BuildAdvice(string name, string driver, string? family, bool mismatch, string? deviceId)
    {
        var parts = new List<string>
        {
            driver.Length > 0 ? Loc.T("Gpu.Driver", driver) : Loc.T("Gpu.DriverUnknown"),
        };

        if (mismatch)
        {
            var actual = FamilyFromDeviceId(deviceId);
            var series = actual switch
            {
                "Ampere" => Loc.T("Gpu.SeriesAmpere"),
                "Turing" => Loc.T("Gpu.SeriesTuring"),
                "Ada" => Loc.T("Gpu.SeriesAda"),
                "Blackwell" => Loc.T("Gpu.SeriesBlackwell"),
                _ => "",
            };

            parts.Add(Loc.T("Gpu.Mismatch", name, deviceId, actual, series, RouteForFamily(actual)));
        }

        parts.Add(family switch
        {
            "Ada" or "Blackwell" => Loc.T("Gpu.AdviceAda"),
            "Ampere" => Loc.T("Gpu.AdviceAmpere"),
            "Turing" => Loc.T("Gpu.AdviceTuring"),
            _ => Loc.T("Gpu.AdviceUnknown"),
        });

        return string.Join(" ", parts);
    }
}
