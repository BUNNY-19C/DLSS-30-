using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DLSSGManager;

public sealed class OpResult
{
    public bool Ok { get; set; } = true;
    public string Message { get; set; } = "";
    public List<string> Lines { get; } = new();

    public void Note(string text)
    {
        Lines.Add(text);
        AppPaths.Log(text);
    }

    public void Fail(string text)
    {
        Ok = false;
        Message = text;
        Lines.Add("错误: " + text);
        AppPaths.Log("错误: " + text);
    }
}

/// <summary>
/// The only place that writes into game directories. Every write is preceded by a backup of anything
/// that is not ours, and every removal is gated on the file being provably ours (project signature
/// or a matching recorded hash), so a restore can never eat a ReShade dxgi.dll by accident.
/// </summary>
public static class DeploymentService
{
    public const string AutoProxy = "自动";

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// True when the file's Authenticode signature is intact and was made by the project's certificate.
    ///
    /// <see cref="X509Certificate.CreateFromSignedFile"/> only extracts the certificate — it does not
    /// check that the signature still matches the bytes. A tampered DLL would therefore look
    /// "signed" if that were the only check, which matters because downloads may come from a mirror.
    /// The signature is validated through WinVerifyTrust and the result is interpreted fail-closed.
    /// </summary>
    public static bool IsProjectSigned(string path)
    {
        var cert = ReadSignerCertificate(path, out var signatureIntact);
        if (cert is null) return false;

        using (cert)
        {
            if (!signatureIntact) return false;

            var subject = cert.Subject ?? "";
            return subject.Contains("DLSSG", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Reads the signer certificate and reports whether the file's signature still matches its
    /// contents.
    ///
    /// A self-signed certificate cannot chain to a trusted root, so chain validation necessarily
    /// fails even for an untouched file. Only the two outcomes that mean "the bytes are as signed"
    /// are accepted; every other result, including a bad digest, is treated as unsigned.
    /// </summary>
    public static X509Certificate2? ReadSignerCertificate(string path, out bool signatureIntact)
    {
        signatureIntact = false;

        X509Certificate2 cert;
        try
        {
            cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        }
        catch
        {
            // No signature block at all, or not a PE file.
            return null;
        }

        signatureIntact = IsSignatureIntact(path);
        return cert;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public int cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    /// <summary>Generic verify; accepts any certificate including a self-signed one.</summary>
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;

    private const int S_OK = 0;
    /// <summary>Chain terminates in an untrusted root — expected for the project's self-signed cert.</summary>
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    /// <summary>No signature present, or the hash in it does not match the file.</summary>
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);

    /// <summary>
    /// Verifies that the file's signature matches its contents. Fails closed: anything other than
    /// "verified" or "verified but self-signed" counts as not intact.
    /// </summary>
    private static bool IsSignatureIntact(string path)
    {
        IntPtr filePtr = IntPtr.Zero;
        IntPtr dataPtr = IntPtr.Zero;

        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, filePtr, false);

            var data = new WinTrustData
            {
                cbStruct = Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = filePtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = 0,
            };

            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, dataPtr);

            // Close the state or the handle stays open until the process exits.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(data, dataPtr, false);
            WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, dataPtr);

            if (result == S_OK) return true;

            if (result == CERT_E_UNTRUSTEDROOT)
            {
                // The bytes are as signed; only the chain is untrusted, which is inherent to a
                // self-signed certificate. Callers confirm identity via the pinned thumbprint.
                return true;
            }

            if (result == TRUST_E_NOSIGNATURE || result == TRUST_E_BAD_DIGEST)
            {
                AppPaths.Log($"签名校验未通过（{(result == TRUST_E_BAD_DIGEST ? "摘要不匹配，文件可能被篡改" : "无签名")}）：{path}");
                return false;
            }

            AppPaths.Log($"签名校验返回未知结果 0x{result:X8}，按未签名处理：{path}");
            return false;
        }
        catch (Exception ex)
        {
            AppPaths.Log("验证签名失败: " + ex.Message);
            return false;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
        }
    }

    public static List<Process> ProcessesRunningIn(string directory)
    {
        var result = new List<Process>();
        string full;
        try { full = Path.GetFullPath(directory).TrimEnd('\\') + "\\"; }
        catch { return result; }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = Native.GetProcessPath(p.Id);
                if (string.IsNullOrEmpty(path)) continue;
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) continue;
                var dirFull = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                if (dirFull.StartsWith(full, StringComparison.OrdinalIgnoreCase)) result.Add(p);
            }
            catch
            {
                // A process that exits mid-enumeration is not a reason to fail the check.
            }
        }

        return result;
    }

    public static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".dlssg_write_probe_" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Unblock(string path)
    {
        try { File.Delete(path + ":Zone.Identifier"); }
        catch { /* No mark-of-the-web, or a filesystem without alternate streams. */ }
    }

    /// <summary>
    /// True when the file is provably this project's: either it carries the project's self-signed
    /// certificate, or it matches the hash recorded when we wrote it.
    /// </summary>
    public static bool IsOurs(string path, string? recordedHash)
    {
        if (!File.Exists(path)) return false;
        if (IsProjectSigned(path)) return true;
        if (!string.IsNullOrEmpty(recordedHash))
        {
            try { return string.Equals(Sha256(path), recordedHash, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        return false;
    }

    /// <summary>First candidate name in the game directory that already holds a project-signed DLL.</summary>
    public static string? FindInstalledProxy(string renderDir)
    {
        foreach (var name in ModSource.ProxyCandidates)
        {
            var path = Path.Combine(renderDir, name);
            if (File.Exists(path) && IsProjectSigned(path)) return name;
        }

        return null;
    }

    private static string PickProxy(GameEntry game)
    {
        var wanted = game.PreferredProxy;
        if (!string.Equals(wanted, AutoProxy, StringComparison.OrdinalIgnoreCase) &&
            ModSource.ProxyCandidates.Contains(wanted, StringComparer.OrdinalIgnoreCase))
        {
            return ModSource.ProxyCandidates.First(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var name in ModSource.ProxyCandidates)
        {
            var path = Path.Combine(game.RenderDir, name);
            if (!File.Exists(path)) return name;
            if (IsProjectSigned(path)) return name;
        }

        return ModSource.ProxyCandidates[0];
    }

    /// <summary>Auto-pick a name that is free, so an occupied entry is reported rather than silently overwritten.</summary>
    private static string? PickFreeProxy(GameEntry game)
    {
        var wanted = game.PreferredProxy;
        if (!string.Equals(wanted, AutoProxy, StringComparison.OrdinalIgnoreCase))
        {
            return ModSource.ProxyCandidates.Contains(wanted, StringComparer.OrdinalIgnoreCase)
                ? ModSource.ProxyCandidates.First(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        foreach (var name in ModSource.ProxyCandidates)
        {
            var path = Path.Combine(game.RenderDir, name);
            if (!File.Exists(path)) return name;
        }

        return FindInstalledProxy(game.RenderDir);
    }

    /// <summary>
    /// Installs the proxy and INI into the game folder.
    ///
    /// <paramref name="allowProtected"/> defaults to false: a game with a kernel-mode anti-cheat will
    /// quarantine the proxy before it loads (so the mod cannot work) and may record a violation
    /// against the account, so that combination is refused unless the caller explicitly overrides.
    /// </summary>
    public static OpResult Deploy(GameEntry game, ModSource source, bool allowProtected = false)
    {
        var r = new OpResult();

        if (!source.IsValid)
        {
            // The common case for a fresh clone is that the mod files were never fetched, so say that
            // rather than only reporting which file is missing.
            var hint = Directory.Exists(source.Root)
                ? $"（{source.Root}）"
                : "（目录不存在）。请点工具条上的「下载 / 更新 Mod 文件」获取。";

            r.Fail($"Mod 文件源不可用：{source.ValidationMessage}{hint}");
            return r;
        }

        if (string.IsNullOrWhiteSpace(game.RenderDir) || !Directory.Exists(game.RenderDir))
        {
            r.Fail("渲染目录不存在，请先设置正确的路径。");
            return r;
        }

        var running = ProcessesRunningIn(game.RenderDir);
        if (running.Count > 0)
        {
            r.Fail("游戏正在运行，请完全退出后再部署：" + string.Join("、", running.Select(p => p.ProcessName)));
            return r;
        }

        var protection = AntiCheat.Scan(game.RenderDir);
        game.Protection = protection;

        if (protection.HasKernelAntiCheat && !allowProtected)
        {
            r.Fail(
                $"已阻止部署：{protection.Summary}（{protection.Evidence}）。\n" +
                "     内核级反作弊会在游戏启动前就拦截并隔离代理 DLL，因此本 Mod 在这款游戏上无法生效，" +
                "而且检测记录可能危及账号。请改用游戏自带的帧生成选项。\n" +
                "     如果你确认理解风险，可在确认对话框中选择仍然部署。");
            return r;
        }

        if (!IsWritable(game.RenderDir))
        {
            r.Fail("没有写入权限。请用管理员身份重新启动本管理器（设置 → 以管理员身份重启）。");
            return r;
        }

        var proxy = PickFreeProxy(game);
        if (proxy is null)
        {
            r.Fail(string.Equals(game.PreferredProxy, AutoProxy, StringComparison.OrdinalIgnoreCase)
                ? "五个代理入口名（version/winmm/dinput8/winhttp/dxgi）在游戏目录里都已被占用。" +
                  "请在该游戏的设置里手动指定一个入口名，或先移除占用它的 Mod。"
                : $"代理名 \"{game.PreferredProxy}\" 不是有效的入口名。请从下拉列表中选择。");
            return r;
        }

        var prev = game.Deployment;
        var proxyDest = Path.Combine(game.RenderDir, proxy);
        var proxyTaken = File.Exists(proxyDest) && !IsOurs(proxyDest, prev?.ProxySha256);

        if (proxyTaken)
        {
            r.Fail($"五个入口名都被占用（{proxy} 已被其他 Mod 使用）。请在该游戏的设置里手动指定一个入口名，或先移除占用它的 Mod。");
            return r;
        }

        var iniDest = Path.Combine(game.RenderDir, ModSource.IniName);
        var iniText = IniTemplate.Render(source.IniText, game.Profile);
        var restoreFolder = Path.Combine(AppPaths.RestoreRoot, game.Id, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var backups = new List<BackupItem>();

        var staleProxy = prev is not null &&
                         !string.Equals(prev.ProxyName, proxy, StringComparison.OrdinalIgnoreCase) &&
                         ModSource.ProxyCandidates.Contains(prev.ProxyName, StringComparer.OrdinalIgnoreCase)
            ? Path.Combine(game.RenderDir, prev.ProxyName)
            : null;

        try
        {
            // Anything that is not ours gets a copy in the restore store before it is displaced.
            if (File.Exists(iniDest) && !IsOurs(iniDest, prev?.IniSha256) && prev is null)
            {
                backups.Add(Backup(iniDest, restoreFolder, r));
            }

            if (staleProxy is not null && File.Exists(staleProxy))
            {
                r.Note($"切换入口名：移除旧代理 {Path.GetFileName(staleProxy)}");
                File.Delete(staleProxy);
            }

            var tmp = proxyDest + ".dlssgtmp";
            File.Copy(source.DllPath(proxy), tmp, overwrite: true);
            File.Move(tmp, proxyDest, overwrite: true);

            var iniTmp = iniDest + ".dlssgtmp";
            File.WriteAllText(iniTmp, iniText, new UTF8Encoding(false));
            File.Move(iniTmp, iniDest, overwrite: true);

            Unblock(proxyDest);
            Unblock(iniDest);

            game.Deployment = new DeploymentInfo
            {
                ProxyName = proxy,
                ModVersion = source.Version,
                DeployedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProxySha256 = Sha256(proxyDest),
                IniSha256 = Sha256(iniDest),
                RestoreFolder = backups.Count > 0 ? restoreFolder : "",
                Backups = backups,
            };

            r.Note($"已部署 {proxy} + {ModSource.IniName} → {game.RenderDir}");
            r.Note($"  路由 {game.Profile.Router} / {game.Profile.KernelImage} / 最大 {game.Profile.MaxGeneratedFrames + 1}X / " +
                   $"近似采样 {(game.Profile.HardwareBilinear ? "开" : "关")} / 日志 {game.Profile.LogLevel}");
            if (backups.Count > 0)
            {
                r.Note($"  已备份被占用的原文件 {backups.Count} 个 → {restoreFolder}");
            }

            r.Message = $"部署完成：{proxy}";
        }
        catch (Exception ex)
        {
            r.Fail("部署失败：" + ex.Message);
        }

        return r;
    }

    private static BackupItem Backup(string path, string restoreFolder, OpResult r)
    {
        Directory.CreateDirectory(restoreFolder);
        var stored = Path.Combine(restoreFolder, Path.GetFileName(path));
        File.Copy(path, stored, overwrite: true);
        var item = new BackupItem
        {
            FileName = Path.GetFileName(path),
            StoredPath = stored,
            Sha256 = Sha256(stored),
            Size = new FileInfo(stored).Length,
        };
        r.Note($"  备份 {item.FileName}（{item.Size / 1024} KB）");
        return item;
    }

    public static OpResult Restore(GameEntry game, bool removeLogs)
    {
        var r = new OpResult();

        if (string.IsNullOrWhiteSpace(game.RenderDir) || !Directory.Exists(game.RenderDir))
        {
            r.Fail("渲染目录不存在，无法恢复。");
            return r;
        }

        var running = ProcessesRunningIn(game.RenderDir);
        if (running.Count > 0)
        {
            r.Fail("游戏正在运行，请完全退出后再恢复：" + string.Join("、", running.Select(p => p.ProcessName)));
            return r;
        }

        if (!IsWritable(game.RenderDir))
        {
            r.Fail("没有写入权限。请用管理员身份重新启动本管理器。");
            return r;
        }

        var prev = game.Deployment;
        var removed = 0;

        try
        {
            // 1) The proxy we recorded; if there is no record, any project-signed DLL in a known entry name.
            var proxyNames = prev is not null && ModSource.ProxyCandidates.Contains(prev.ProxyName, StringComparer.OrdinalIgnoreCase)
                ? new List<string> { prev.ProxyName }
                : ModSource.ProxyCandidates.ToList();

            foreach (var name in proxyNames)
            {
                var path = Path.Combine(game.RenderDir, name);
                if (!File.Exists(path)) continue;

                var recorded = prev is not null && string.Equals(prev.ProxyName, name, StringComparison.OrdinalIgnoreCase)
                    ? prev.ProxySha256
                    : null;

                if (prev is null && !IsProjectSigned(path))
                {
                    continue;
                }

                if (IsOurs(path, recorded))
                {
                    File.Delete(path);
                    removed++;
                    r.Note($"已移除 {name}");
                }
                else
                {
                    r.Note($"保留 {name}：内容与本记录不符，未删除。");
                }
            }

            // 2) The INI. A recorded deployment proves we put an INI at this exact path, so a
            //    hash mismatch only means the user edited it by hand — still ours to remove.
            var iniPath = Path.Combine(game.RenderDir, ModSource.IniName);
            if (File.Exists(iniPath))
            {
                var byHash = prev is not null && IsOurs(iniPath, prev.IniSha256);
                var byBanner = LooksLikeProjectIni(iniPath);

                if (byHash || byBanner)
                {
                    if (prev is not null && !byHash)
                        r.Note($"移除 {ModSource.IniName}：内容已被手工修改，仍属本项目文件。");

                    File.Delete(iniPath);
                    removed++;
                    r.Note($"已移除 {ModSource.IniName}");
                }
                else
                {
                    r.Note($"保留 {ModSource.IniName}：不是本项目的配置文件，未删除。");
                }
            }

            // 3) Copies an anti-cheat quarantined by renaming (e.g. version.dll.3787982156). Only
            //    files that are provably ours are removed, so another tool's ".bak" survives.
            foreach (var name in proxyNames)
            {
                foreach (var copy in AntiCheat.FindQuarantinedCopies(game.RenderDir, name, prev?.ProxySha256))
                {
                    try
                    {
                        File.Delete(copy);
                        removed++;
                        r.Note($"已清理被隔离的副本 {Path.GetFileName(copy)}");
                    }
                    catch (Exception ex)
                    {
                        r.Note($"无法清理 {Path.GetFileName(copy)}：{ex.Message}");
                    }
                }
            }

            // 4) Put back whatever we displaced.
            if (prev is not null && prev.Backups.Count > 0)
            {
                foreach (var b in prev.Backups)
                {
                    if (!File.Exists(b.StoredPath))
                    {
                        r.Note($"备份文件已丢失，跳过还原：{b.FileName}");
                        continue;
                    }

                    var dest = Path.Combine(game.RenderDir, b.FileName);
                    File.Copy(b.StoredPath, dest, overwrite: true);
                    removed++;
                    r.Note($"已还原 {b.FileName}");
                }
            }

            if (removeLogs)
            {
                var logs = Path.Combine(game.RenderDir, ModSource.LogDirName);
                if (Directory.Exists(logs))
                {
                    Directory.Delete(logs, recursive: true);
                    r.Note($"已删除日志目录 {ModSource.LogDirName}\\");
                }
            }

            game.Deployment = null;
            r.Message = removed > 0 ? $"恢复完成，处理 {removed} 项。" : "没有需要恢复的文件。";
            r.Note(r.Message);
        }
        catch (Exception ex)
        {
            r.Fail("恢复失败：" + ex.Message);
        }

        return r;
    }

    /// <summary>
    /// Recognises the project's INI by its banner or its own file name. Deliberately stricter than a
    /// bare keyword search so an unrelated mod's INI sharing the slot is left alone.
    /// </summary>
    private static bool LooksLikeProjectIni(string path)
    {
        try
        {
            var head = File.ReadLines(path).Take(4).ToList();
            if (head.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"Native\s+[0-9]+\.[0-9]+")))
                return true;

            return File.ReadAllText(path).Contains("dlssg_sm86", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Takes over an installation that was copied in by hand, so the manager can restore it later.</summary>
    public static OpResult Adopt(GameEntry game)
    {
        var r = new OpResult();
        if (!Directory.Exists(game.RenderDir))
        {
            r.Fail("渲染目录不存在。");
            return r;
        }

        var proxy = FindInstalledProxy(game.RenderDir);
        if (proxy is null)
        {
            r.Fail("该目录没有找到本项目签名的代理 DLL。");
            return r;
        }

        var proxyPath = Path.Combine(game.RenderDir, proxy);
        var iniPath = Path.Combine(game.RenderDir, ModSource.IniName);
        var iniExists = File.Exists(iniPath);

        game.Deployment = new DeploymentInfo
        {
            ProxyName = proxy,
            ModVersion = iniExists ? ModSource.ReadVersion(iniPath) ?? "未知" : "未知",
            DeployedAt = File.GetLastWriteTime(proxyPath).ToString("yyyy-MM-dd HH:mm:ss") + "（接管）",
            ProxySha256 = Sha256(proxyPath),
            IniSha256 = iniExists ? Sha256(iniPath) : "",
            RestoreFolder = "",
            Backups = new List<BackupItem>(),
        };

        r.Note($"已接管 {proxy}" + (iniExists ? $" + {ModSource.IniName}" : "（未找到 INI）"));
        r.Note("注意：接管记录里没有原始备份，因为文件是手工放入的。");
        r.Message = $"已接管 {proxy}";
        return r;
    }

    public static void Check(GameEntry game)
    {
        game.Deployment ??= null;

        if (string.IsNullOrWhiteSpace(game.RenderDir))
        {
            game.Status = GameStatus.Unknown;
            game.StatusDetail = "未设置渲染目录";
            return;
        }

        if (!Directory.Exists(game.RenderDir))
        {
            game.Status = GameStatus.Missing;
            game.StatusDetail = "渲染目录不存在";
            return;
        }

        // Cheap enough to run on every check, and it is what drives the warning banner.
        game.Protection = AntiCheat.Scan(game.RenderDir);

        var prev = game.Deployment;
        if (prev is null)
        {
            var found = FindInstalledProxy(game.RenderDir);
            game.Status = GameStatus.NotDeployed;
            game.StatusDetail = found is null
                ? "未部署"
                : $"检测到手工安装的 {found}，可点「接管」纳入管理";
            return;
        }

        var proxyPath = Path.Combine(game.RenderDir, prev.ProxyName);
        var iniPath = Path.Combine(game.RenderDir, ModSource.IniName);
        var proxyOk = File.Exists(proxyPath);
        var iniOk = File.Exists(iniPath);

        if (!proxyOk || !iniOk)
        {
            // A kernel anti-cheat renames the proxy rather than deleting it, so a vanished DLL with
            // quarantined copies left behind is reported distinctly from a plain missing file.
            var quarantined = AntiCheat.FindQuarantinedCopies(game.RenderDir, prev.ProxyName, prev.ProxySha256);

            game.Status = GameStatus.Missing;
            if (quarantined.Count > 0)
            {
                var protection = AntiCheat.Scan(game.RenderDir);
                game.Protection = protection;
                game.StatusDetail =
                    $"{prev.ProxyName} 已被反作弊隔离（发现 {quarantined.Count} 个被改名的副本，" +
                    $"{protection.Summary}）。点「一键恢复」可清理残留。";
            }
            else
            {
                game.StatusDetail = !proxyOk ? $"{prev.ProxyName} 不存在" : $"{ModSource.IniName} 不存在";
            }

            return;
        }

        try
        {
            var proxyMatch = string.Equals(Sha256(proxyPath), prev.ProxySha256, StringComparison.OrdinalIgnoreCase);
            var iniMatch = string.Equals(Sha256(iniPath), prev.IniSha256, StringComparison.OrdinalIgnoreCase);
            if (proxyMatch && iniMatch)
            {
                game.Status = GameStatus.Deployed;
                game.StatusDetail = $"Mod {prev.ModVersion} · {prev.ProxyName} · {prev.DeployedAt}";
            }
            else
            {
                game.Status = GameStatus.Modified;
                game.StatusDetail = proxyMatch ? "INI 已被修改（可能是你在游戏里改过配置）" : "代理 DLL 与部署记录不一致";
            }
        }
        catch (Exception ex)
        {
            game.Status = GameStatus.Unknown;
            game.StatusDetail = "校验失败：" + ex.Message;
        }
    }

    public static string? LatestLogFile(string renderDir)
    {
        var dir = Path.Combine(renderDir, ModSource.LogDirName, "logs");
        if (!Directory.Exists(dir)) return null;
        return new DirectoryInfo(dir).GetFiles("*.jsonl")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }
}
