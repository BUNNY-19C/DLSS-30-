using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;

namespace DLSSGManager;

/// <summary>
/// Downloads the project archive from GitHub. Every request is pinned to HTTPS on an allow-listed
/// host whose resolved addresses must all be public, redirects are followed manually under the same
/// checks, and the response is size-capped; the app never talks to anywhere else.
/// </summary>
public static class ModFetcher
{
    private static readonly string[] AllowedHosts =
    {
        "github.com",
        "codeload.github.com",
        "raw.githubusercontent.com",
        "api.github.com",
    };

    private const long MaxArchiveBytes = 256L * 1024 * 1024;

    private const string ArchiveUrl = "https://codeload.github.com/sdli1995/dlssg_for_sm86/zip/refs/heads/main";

    public static bool IsAllowedAddress(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return false;

        IPAddress[] addresses;
        try { addresses = Dns.GetHostAddresses(uri.Host); }
        catch { return false; }

        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6) return IsPublicAddress(ip.MapToIPv4());
            if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Loopback)) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                 // fc00::/7 unique local
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return false;  // fe80::/10 link local
            if (b[0] == 0xFF) return false;                           // ff00::/8 multicast
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32 doc
            return true;
        }

        var o = ip.GetAddressBytes();
        return o[0] switch
        {
            0 => false,                                     // 0.0.0.0/8
            10 => false,                                    // private
            127 => false,                                   // loopback
            169 when o[1] == 254 => false,                  // link local
            172 when o[1] >= 16 && o[1] <= 31 => false,     // private
            192 when o[1] == 168 => false,                  // private
            192 when o[1] == 0 && o[2] == 0 => false,       // 192.0.0.0/24
            192 when o[1] == 0 && o[2] == 2 => false,       // TEST-NET-1
            198 when o[1] == 18 || o[1] == 19 => false,     // benchmarking
            198 when o[1] == 51 && o[2] == 100 => false,    // TEST-NET-2
            203 when o[1] == 0 && o[2] == 113 => false,     // TEST-NET-3
            100 when o[1] >= 64 && o[1] <= 127 => false,    // CGNAT
            >= 224 => false,                                // multicast + reserved
            _ => true,
        };
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DLSSGManager/1.0");
        return client;
    }

    /// <summary>Follows at most a handful of hops, re-validating every target.</summary>
    private static async Task<HttpResponseMessage> GetCheckedAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        if (!IsAllowedAddress(uri)) throw new InvalidOperationException($"地址不允许：{uri}");

        var current = uri;
        for (var hop = 0; hop < 5; hop++)
        {
            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidOperationException("重定向缺少目标地址。");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsAllowedAddress(current)) throw new InvalidOperationException($"重定向目标不允许：{current}");
                continue;
            }

            response.EnsureSuccessStatusCode();
            return response;
        }

        throw new InvalidOperationException("重定向次数过多。");
    }

    public static async Task<OpResult> DownloadIntoAsync(string destination, IProgress<string>? progress, CancellationToken ct)
    {
        // GitHub's endpoints reset connections fairly often on some networks (observed ~25% of
        // attempts here), so a transient failure is retried rather than surfaced to the user.
        const int attempts = 4;
        OpResult? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                var wait = TimeSpan.FromSeconds(2 * (attempt - 1));
                progress?.Report($"连接失败，{wait.TotalSeconds:F0} 秒后重试（第 {attempt}/{attempts} 次）…");
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            last = await AttemptDownloadAsync(destination, progress, ct).ConfigureAwait(false);
            if (last.Ok) return last;

            AppPaths.Log($"下载尝试 {attempt}/{attempts} 失败: {last.Message}");
        }

        progress?.Report("多次重试后仍未成功");
        return last!;
    }

    private static async Task<OpResult> AttemptDownloadAsync(string destination, IProgress<string>? progress, CancellationToken ct)
    {
        var r = new OpResult();
        Directory.CreateDirectory(destination);

        var staging = Path.Combine(Path.GetTempPath(), "dlssg_" + Guid.NewGuid().ToString("N"));
        var archive = staging + ".zip";

        try
        {
            progress?.Report("连接 codeload.github.com …");
            using var client = CreateClient();
            using var response = await GetCheckedAsync(client, new Uri(ArchiveUrl), ct).ConfigureAwait(false);

            if (response.Content.Headers.ContentLength is long declared && declared > MaxArchiveBytes)
                throw new InvalidOperationException($"压缩包过大（{declared / 1024 / 1024} MB），已中止。");

            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxArchiveBytes) throw new InvalidOperationException("压缩包超过大小上限，已中止。");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                r.Note($"已下载 {total / 1024 / 1024.0:F1} MB");
            }

            progress?.Report("解压 …");
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(archive, staging, overwriteFiles: true);

            var sourceRoot = Directory.EnumerateDirectories(staging).FirstOrDefault() ?? staging;
            var wantRoot = new[] { "version.dll", "dlssg_sm86.ini", "README.md", "README.en.md", "THIRD_PARTY_NOTICES.txt" };
            var copied = 0;

            foreach (var name in wantRoot)
            {
                var src = Path.Combine(sourceRoot, name);
                if (File.Exists(src))
                {
                    CopyInto(src, Path.Combine(destination, name), destination);
                    copied++;
                }
            }

            foreach (var sub in new[] { "altnative", Path.Combine("config", "presets"), "docs" })
            {
                var srcDir = Path.Combine(sourceRoot, sub);
                if (!Directory.Exists(srcDir)) continue;
                foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(sourceRoot, file);
                    CopyInto(file, Path.Combine(destination, rel), destination);
                    copied++;
                }
            }

            var version = ModSource.ReadVersion(Path.Combine(destination, ModSource.IniName));
            r.Note($"已更新 {copied} 个文件到 {destination}");
            r.Message = version is null ? $"已更新 {copied} 个文件" : $"已更新到 Native {version}";
        }
        catch (Exception ex)
        {
            r.Fail("下载失败：" + ex.Message);
        }
        finally
        {
            TryDelete(archive);
            TryDeleteDirectory(staging);
        }

        return r;
    }

    /// <summary>Rejects any archive entry that would escape the destination folder.</summary>
    private static void CopyInto(string sourceFile, string destinationFile, string destinationRoot)
    {
        var rootFull = Path.GetFullPath(destinationRoot).TrimEnd('\\') + "\\";
        var destFull = Path.GetFullPath(destinationFile);
        if (!destFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("压缩包条目指向目标目录之外。");

        var dir = Path.GetDirectoryName(destFull);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(sourceFile, destFull, overwrite: true);
        DeploymentService.Unblock(destFull);
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}
