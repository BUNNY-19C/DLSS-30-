using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DLSSGManager;

public sealed record GpuInfo(string Name, string Driver, string Router, string Advice);

/// <summary>
/// Display adapter probing through the Win32 display API and the driver registry key. No child
/// process is started for this; it only reads what Windows already knows.
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

    /// <summary>Adapter names as Windows reports them, e.g. "NVIDIA GeForce RTX 3080 Ti".</summary>
    public static List<string> AdapterNames()
    {
        var names = new List<string>();
        var dd = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };

        for (uint i = 0; EnumDisplayDevicesW(null, i, ref dd, 0); i++)
        {
            dd.cb = Marshal.SizeOf<DisplayDevice>();
            if ((dd.StateFlags & AttachedToDesktop) == 0) continue;
            var s = dd.DeviceString ?? "";
            if (s.Length == 0) continue;
            if (s.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)) continue;
            if (!names.Contains(s, StringComparer.OrdinalIgnoreCase)) names.Add(s);
        }

        return names;
    }

    /// <summary>Driver version for a display adapter, read from the display class registry key.</summary>
    public static string DriverVersion(string adapterName)
    {
        const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(displayClass);
            if (key is null) return "";

            foreach (var sub in key.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
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
    /// Maps an adapter name to the mod's route. Pure so it can be tested without the hardware.
    /// </summary>
    public static string RouteForAdapter(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName)) return "SM86";

        // Turing is the only family that needs the older route.
        if (System.Text.RegularExpressions.Regex.IsMatch(adapterName, @"RTX\s*20\d0")) return "SM75";

        return "SM86";
    }

    /// <summary>
    /// Human-readable guidance for an adapter. Separated from probing so the wording can be checked
    /// against any adapter name, not just the one in the current machine.
    /// </summary>
    public static string AdviceForAdapter(string adapterName, string driver)
    {
        var driverText = driver.Length > 0 ? $"驱动 {driver}" : "驱动版本未知";
        var route = RouteForAdapter(adapterName);

        if (System.Text.RegularExpressions.Regex.IsMatch(adapterName, @"RTX\s*50\d0|RTX\s*40\d0"))
            return $"{driverText}。Ada/Blackwell 原生支持 DLSS 帧生成，通常不需要本 Mod。";

        if (System.Text.RegularExpressions.Regex.IsMatch(adapterName, @"RTX\s*30\d0"))
            return $"{driverText}。与本机匹配：SM86 路由即作者实卡验证的路径。";

        if (route == "SM75")
            return $"{driverText}。Turing 请用 SM75 路由；作者仅在 3080 Ti 上做过 PTX 前向检查。";

        return $"{driverText}。未识别的型号，请自行确认路由。";
    }

    public static GpuInfo Probe()
    {
        var adapters = AdapterNames();
        var nvidia = adapters.FirstOrDefault(a => a.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

        if (nvidia is null)
        {
            return new GpuInfo(
                adapters.Count > 0 ? string.Join(" / ", adapters) : "未检测到 NVIDIA 显卡",
                "",
                "SM86",
                adapters.Count == 0
                    ? "未能枚举显示适配器。本 Mod 需要 NVIDIA 驱动提供的 NGX/NVAPI/CUDA 接口。"
                    : "未检测到 NVIDIA 显卡。本 Mod 需要 NVIDIA 驱动提供的 NGX/NVAPI/CUDA 接口。");
        }

        var driver = DriverVersion(nvidia);
        return new GpuInfo(nvidia, driver, RouteForAdapter(nvidia), AdviceForAdapter(nvidia, driver));
    }
}
