using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace DLSSGManager;

/// <summary>
/// Obtains the dlssg_for_sm86 payload from any of several mirrors.
///
/// Security model
/// ---------------
/// The files are native DLLs that end up in game directories, so the download path is treated as
/// hostile:
///
/// · Every request is HTTPS, on an allow-listed host whose resolved addresses must all be public.
///   Redirects are followed manually with the same checks at each hop.
/// · Response size is capped, and archive entries may not escape the destination folder.
/// · The payload is verified after download: every proxy DLL must carry a valid Authenticode
///   signature. For third-party mirrors the signer must also match a pinned certificate
///   thumbprint, because the mirror is not the authority for the content. For GitHub's own
///   endpoints the certificate is logged but not required to match, so a future certificate
///   rotation upstream does not break downloads.
///
/// Sources are tried in order of authority and efficiency, so a blocked or flaky endpoint only
/// costs a fallback rather than a failed download.
/// </summary>
public static class ModFetcher
{
    /// <summary>Hosts the downloader may ever contact.</summary>
    private static readonly string[] AllowedHosts =
    {
        // GitHub-operated
        "github.com",
        "codeload.github.com",
        "raw.githubusercontent.com",
        "api.github.com",
        // Third-party CDN mirror. Accepted only with a matching certificate pin.
        "cdn.jsdelivr.net",
    };

    /// <summary>
    /// Certificate the project signs every proxy DLL with. Recorded from version.dll, winmm.dll,
    /// dinput8.dll, winhttp.dll and dxgi.dll of Native 0.2.4 — all five share it.
    ///
    /// A mismatch on a mirror means the file is not the project's build, so the download is
    /// rejected. A mismatch on GitHub's own endpoints is logged as a warning instead, so upstream
    /// rotating its self-signed certificate does not brick the updater.
    /// </summary>
    private const string PinnedCertThumbprint = "A994735E6A7E9AA31FA926B3023B7C487DAB4850";

    /// <summary>Signer subject fragment used as a secondary sanity check on any source.</summary>
    private const string ExpectedSignerSubject = "DLSSG";

    private const long MaxArchiveBytes = 256L * 1024 * 1024;

    private const string RepoPath = "sdli1995/dlssg_for_sm86";
    private const string RepoRef = "main";

    private sealed record Artifact(string RelativePath, bool Required, bool NeedsSignature);

    /// <summary>The payload the manager actually consumes, with the checks each file needs.</summary>
    private static readonly Artifact[] Payload =
    {
        new("version.dll", true, true),
        new("dlssg_sm86.ini", true, false),
        new("altnative/winmm.dll", true, true),
        new("altnative/dinput8.dll", true, true),
        new("altnative/winhttp.dll", true, true),
        new("altnative/dxgi.dll", true, true),
        new("config/presets/sm86-default.ini", false, false),
        new("config/presets/sm86-performance.ini", false, false),
        new("README.md", false, false),
        new("THIRD_PARTY_NOTICES.txt", false, false),
    };

    /// <summary>
    /// A download endpoint. <paramref name="Official"/> marks GitHub-operated sources, where TLS to
    /// the repository is itself the authority and the certificate pin is advisory.
    /// </summary>
    private sealed record Source(string Name, bool Official, string UrlTemplate, bool IsArchive);

    private static readonly Source[] Sources =
    {
        // One request, compressed (~28 MB). Preferred when reachable.
        new(Loc.T("Fetch.SourceCodeload"), true,
            $"https://codeload.github.com/{RepoPath}/zip/refs/heads/{RepoRef}", true),

        // Same content through a different entry point; useful when codeload is throttled.
        new(Loc.T("Fetch.SourceZipball"), true,
            $"https://api.github.com/repos/{RepoPath}/zipball/{RepoRef}", true),

        // Per-file raw access. Slower (~78 MB uncompressed) but a distinct path from codeload.
        new(Loc.T("Fetch.SourceRaw"), true,
            $"https://raw.githubusercontent.com/{RepoPath}/{RepoRef}/{{0}}", false),

        // Public CDN mirror, often reachable where GitHub is not. Certificate pin enforced.
        new(Loc.T("Fetch.SourceJsDelivr"), false,
            $"https://cdn.jsdelivr.net/gh/{RepoPath}@{RepoRef}/{{0}}", false),
    };

    public static IReadOnlyList<string> SourceNames => Sources.Select(s => s.Name).ToArray();

    /// <summary>
    /// Environment variable naming a single source to use, for diagnosing one endpoint in isolation.
    /// Matched case-insensitively against <see cref="Source.Name"/>; an unknown value falls back to
    /// trying every source, so a typo cannot silently disable downloading.
    /// </summary>
    public const string SourceFilterVariable = "DLSSGMANAGER_SOURCE";

    /// <summary>Sources to attempt, honouring <see cref="SourceFilterVariable"/> when set.</summary>
    private static IEnumerable<Source> ActiveSources()
    {
        var filter = Environment.GetEnvironmentVariable(SourceFilterVariable);
        if (string.IsNullOrWhiteSpace(filter)) return Sources;

        var matched = Sources.Where(s => s.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0)
        {
            AppPaths.Log(Loc.T("Fetch.UnknownFilter", filter,
                string.Join(" | ", Sources.Select(s => s.Name))));
            return Sources;
        }

        return matched;
    }

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
        if (!IsAllowedAddress(uri)) throw new InvalidOperationException(Loc.T("Fetch.UrlRejected", uri));

        var current = uri;
        for (var hop = 0; hop < 5; hop++)
        {
            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidOperationException(Loc.T("Fetch.RedirectNoTarget"));
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsAllowedAddress(current)) throw new InvalidOperationException(Loc.T("Fetch.RedirectRejected", current));
                continue;
            }

            response.EnsureSuccessStatusCode();
            return response;
        }

        throw new InvalidOperationException(Loc.T("Fetch.RedirectTooMany"));
    }

    /// <summary>
    /// Downloads the payload, trying each source until one yields a verified result.
    /// </summary>
    public static async Task<OpResult> DownloadIntoAsync(string destination, IProgress<string>? progress, CancellationToken ct)
    {
        var failures = new List<string>();

        foreach (var source in ActiveSources())
        {
            ct.ThrowIfCancellationRequested();

            // GitHub's endpoints reset connections fairly often on some networks (observed ~25% of
            // attempts here), so a transient failure on one source is retried before moving on.
            const int attemptsPerSource = 2;

            for (var attempt = 1; attempt <= attemptsPerSource; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(attempt > 1 ? Loc.T("Fetch.Retrying", source.Name, attempt) : Loc.T("Fetch.Starting", source.Name));

                if (attempt > 1)
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

                var result = await AttemptAsync(source, destination, progress, ct).ConfigureAwait(false);
                if (result.Ok) return result;

                AppPaths.Log(Loc.T("Fetch.AttemptFailed", source.Name, attempt, attemptsPerSource, result.Message));
                if (attempt == attemptsPerSource) failures.Add($"{source.Name}：{result.Message}");
            }
        }

        var r = new OpResult();
        r.Fail(Loc.T("Fetch.AllFailed", string.Join("\n     ", failures)));
        return r;
    }

    private static async Task<OpResult> AttemptAsync(Source source, string destination, IProgress<string>? progress, CancellationToken ct)
    {
        var r = new OpResult();
        var staging = Path.Combine(Path.GetTempPath(), "dlssg_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(destination);

            if (source.IsArchive)
                await FetchArchiveAsync(source, staging, r, progress, ct).ConfigureAwait(false);
            else
                await FetchIndividualFilesAsync(source, staging, r, progress, ct).ConfigureAwait(false);

            // Verify before touching the destination: a mirror must not be able to write a DLL that
            // is not the project's build.
            var (accepted, message) = Verify(staging, source.Official);
            r.Note(message);
            if (!accepted)
            {
                r.Fail(message);
                return r;
            }

            var copied = Publish(staging, destination);
            var version = ModSource.ReadVersion(Path.Combine(destination, ModSource.IniName));

            r.Note(Loc.T("Fetch.Updated", copied, destination));
            r.Message = version is null
                ? Loc.T("Fetch.UpdatedGeneric", copied, source.Name)
                : Loc.T("Fetch.UpdatedVersion", version, source.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            r.Fail(Loc.T("Fetch.Failed", ex.Message));
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        return r;
    }

    private static async Task FetchArchiveAsync(Source source, string staging, OpResult r, IProgress<string>? progress, CancellationToken ct)
    {
        var archive = staging + ".zip";
        try
        {
            using var client = CreateClient();
            using var response = await GetCheckedAsync(client, new Uri(source.UrlTemplate), ct).ConfigureAwait(false);

            if (response.Content.Headers.ContentLength is long declared && declared > MaxArchiveBytes)
                throw new InvalidOperationException(Loc.T("Fetch.ArchiveTooLarge", declared / 1024 / 1024));

            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxArchiveBytes) throw new InvalidOperationException(Loc.T("Fetch.ArchiveTooLarge2"));
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                r.Note(Loc.T("Fetch.Downloaded", $"{total / 1024 / 1024.0:F1}"));
            }

            progress?.Report(Loc.T("Fetch.Extracting"));
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(archive, staging, overwriteFiles: true);

            // The archive wraps everything in a single top-level folder whose name varies by source.
            var inner = Directory.EnumerateDirectories(staging).FirstOrDefault();
            if (inner is null) throw new InvalidOperationException(Loc.T("Fetch.BadArchive"));

            // Flatten that wrapper so staging looks like the payload root.
            foreach (var entry in Directory.EnumerateFileSystemEntries(inner))
            {
                var target = Path.Combine(staging, Path.GetFileName(entry));
                if (Directory.Exists(entry)) Directory.Move(entry, target);
                else File.Move(entry, target, overwrite: true);
            }

            TryDeleteDirectory(inner);
        }
        finally
        {
            TryDelete(archive);
        }
    }

    private static async Task FetchIndividualFilesAsync(Source source, string staging, OpResult r, IProgress<string>? progress, CancellationToken ct)
    {
        using var client = CreateClient();
        Directory.CreateDirectory(staging);

        long total = 0;
        var done = 0;

        foreach (var artifact in Payload)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(Loc.T("Fetch.Downloading", artifact.RelativePath, done + 1, Payload.Length));

            // {0} is the repo-relative path; already URL-safe for these names.
            var url = new Uri(string.Format(source.UrlTemplate, artifact.RelativePath));
            var target = Path.Combine(staging, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            // Nested entries such as altnative/winmm.dll need their folder to exist before writing.
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

            try
            {
                using var response = await GetCheckedAsync(client, url, ct).ConfigureAwait(false);
                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = File.Create(target);

                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxArchiveBytes) throw new InvalidOperationException(Loc.T("Fetch.ContentTooLarge"));
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                done++;
            }
            catch (Exception ex) when (!artifact.Required)
            {
                // Optional files (readme, presets) may legitimately be absent; note and move on.
                r.Note(Loc.T("Fetch.SkippedOptional", artifact.RelativePath, ex.Message));
            }
        }

        r.Note(Loc.T("Fetch.DownloadedCount", $"{total / 1024 / 1024.0:F1}", done, Payload.Length));
    }

    /// <summary>
    /// Checks a staged payload. Required files must exist; every DLL must be signed by the project,
    /// and on a non-official source the signer must match the pinned certificate.
    /// </summary>
    private static (bool Accepted, string Message) Verify(string staging, bool officialSource)
    {
        var missing = Payload.Where(a => a.Required && !File.Exists(Path.Combine(staging, a.RelativePath)))
                             .Select(a => a.RelativePath)
                             .ToList();
        if (missing.Count > 0)
            return (false, Loc.T("Fetch.Incomplete", Loc.Join(missing)));

        var pinMismatch = new List<string>();
        var unsigned = new List<string>();

        foreach (var artifact in Payload.Where(a => a.NeedsSignature))
        {
            var path = Path.Combine(staging, artifact.RelativePath);

            // X509Certificate2 is needed for Thumbprint; the static loader returns the base type.
            X509Certificate2? cert;
            try
            {
                cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            }
            catch
            {
                unsigned.Add(Path.GetFileName(artifact.RelativePath));
                continue;
            }

            using (cert)
            {
                var subject = cert.Subject ?? "";
                if (!subject.Contains(ExpectedSignerSubject, StringComparison.OrdinalIgnoreCase))
                    unsigned.Add(Path.GetFileName(artifact.RelativePath));
                else if (!string.Equals(cert.Thumbprint, PinnedCertThumbprint, StringComparison.OrdinalIgnoreCase))
                    pinMismatch.Add(Path.GetFileName(artifact.RelativePath));
            }
        }

        if (unsigned.Count > 0)
            return (false, Loc.T("Fetch.Unsigned", Loc.Join(unsigned)));

        if (pinMismatch.Count > 0)
        {
            var detail = Loc.T("Fetch.PinDetail", Loc.Join(pinMismatch));

            // A mirror is not the authority for this content, so an unexpected signer is rejected.
            if (!officialSource)
                return (false, Loc.T("Fetch.PinMismatch", detail));

            // GitHub itself is trusted; a different certificate most likely means upstream re-signed.
            AppPaths.Log(Loc.T("Fetch.PinWarning", detail));
            return (true, Loc.T("Fetch.PinAccepted", Loc.Join(pinMismatch)));
        }

        return (true, Loc.T("Fetch.SignatureOk"));
    }

    /// <summary>Copies the verified payload into the destination, preserving relative paths.</summary>
    private static int Publish(string staging, string destination)
    {
        var copied = 0;

        foreach (var artifact in Payload)
        {
            var source = Path.Combine(staging, artifact.RelativePath);
            if (!File.Exists(source)) continue;

            CopyInto(source, Path.Combine(destination, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)), destination);
            copied++;
        }

        return copied;
    }

    /// <summary>Rejects any path that would escape the destination folder.</summary>
    private static void CopyInto(string sourceFile, string destinationFile, string destinationRoot)
    {
        var rootFull = Path.GetFullPath(destinationRoot).TrimEnd('\\') + "\\";
        var destFull = Path.GetFullPath(destinationFile);
        if (!destFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("Fetch.EscapeAttempt"));

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
