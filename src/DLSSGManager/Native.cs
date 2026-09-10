using System.Runtime.InteropServices;
using System.Text;

namespace DLSSGManager;

/// <summary>
/// Process enumeration for the "is the game running" gate. QueryFullProcessImageName with
/// PROCESS_QUERY_LIMITED_INFORMATION also resolves elevated processes, unlike Process.MainModule.
/// </summary>
public static class Native
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr handle, uint flags, StringBuilder buffer, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static string? GetProcessPath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Restarts the manager through the UAC prompt so a write-protected game folder can be written.</summary>
    public static bool RelaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,   // Required for the runas verb; no shell string is involved.
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch
        {
            // The user declined the UAC prompt.
            return false;
        }
    }
}
