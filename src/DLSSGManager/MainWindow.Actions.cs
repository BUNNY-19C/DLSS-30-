using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DLSSGManager;

/// <summary>Deployment, restore and scan handlers.</summary>
public partial class MainWindow
{
    // ---- single-game deployment --------------------------------------------

    private void Deploy_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        if (string.IsNullOrWhiteSpace(game.RenderDir) || !Directory.Exists(game.RenderDir))
        {
            _log.Write("✗ 请先设置有效的渲染目录。");
            return;
        }

        RunDeploy(game);
    }

    private void RunDeploy(GameEntry game)
    {
        if (_busy) { _log.Write("有操作正在进行，请稍候。"); return; }

        var allowProtected = false;
        var protection = AntiCheat.Scan(game.RenderDir);
        game.Protection = protection;

        if (protection.HasKernelAntiCheat)
        {
            var body =
                $"「{game.Name}」带有内核级反作弊：{protection.Products}\n\n" +
                $"证据：{protection.Evidence}\n\n" +
                "这类反作弊会在游戏启动前拦截并隔离代理 DLL，所以：\n" +
                "· 本 Mod 在这款游戏上无法生效；\n" +
                "· 检测记录可能危及账号安全。\n\n" +
                "该游戏目录里有 nvngx_dlssg.dll，说明游戏自带帧生成，建议直接用它。\n\n" +
                "确定仍要部署吗？（不推荐）";

            var answer = MessageBox.Show(body, "检测到内核级反作弊", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                _log.Write($"已取消部署 {game.Name}：{protection.Summary}（{protection.Evidence}）");
                return;
            }

            allowProtected = true;
            _log.Write($"⚠ 用户确认在受保护游戏上部署：{game.Name} — {protection.Summary}");
        }

        _busy = true;
        try
        {
            _log.Write($"— 部署 {game.Name}");
            _log.Write("  " + game.RenderDir);

            var result = DeploymentService.Deploy(game, CurrentSource(), allowProtected);
            _log.Details(result.Lines);
            _log.Result(result.Ok, result.Message);
        }
        finally
        {
            _busy = false;
        }

        LibraryStore.Save(_data);
        FinishGameAction(game);
    }

    /// <summary>
    /// Re-reads the game from disk and repaints its row. Without the re-check the list would keep
    /// showing the status from before the operation ("文件缺失" after a successful restore, etc.).
    /// </summary>
    private void FinishGameAction(GameEntry game)
    {
        DeploymentService.Check(game);
        LibraryStore.Save(_data);
        UpdateStatusCard();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        if (game.Deployment is null)
        {
            var body = $"「{game.Name}」没有部署记录。\n\n" +
                       "将扫描该目录，只删除签名属于本项目的文件（若有）。继续吗？";
            if (MessageBox.Show(body, "确认恢复", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }

        RunRestore(game);
    }

    private void RunRestore(GameEntry game)
    {
        if (_busy) { _log.Write("有操作正在进行，请稍候。"); return; }

        _busy = true;
        try
        {
            _log.Write($"— 恢复 {game.Name}");
            var result = DeploymentService.Restore(game, RemoveLogsCheck.IsChecked == true);
            _log.Details(result.Lines);
            _log.Result(result.Ok, result.Message);
        }
        finally
        {
            _busy = false;
        }

        FinishGameAction(game);
    }

    private void Adopt_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var body = "接管会把当前目录里已存在的本项目 DLL 登记为「由本管理器安装」。\n\n" +
                   "适用于你之前手工复制过 Mod 的情况。接管记录不含原始备份，之后恢复只能删除本项目的文件。\n\n继续吗？";
        if (MessageBox.Show(body, "接管手工安装", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;

        _busy = true;
        try
        {
            var result = DeploymentService.Adopt(game);
            _log.Details(result.Lines);
            _log.Result(result.Ok, result.Message);
        }
        finally
        {
            _busy = false;
        }

        FinishGameAction(game);
    }

    // ---- batch --------------------------------------------------------------

    private void DeployAll_Click(object sender, RoutedEventArgs e)
    {
        var targets = _data.Games
            .Where(g => !string.IsNullOrWhiteSpace(g.RenderDir) && Directory.Exists(g.RenderDir))
            .ToList();

        if (targets.Count == 0) { _log.Write("没有可部署的游戏。"); return; }

        var protectedGames = targets.Where(g => g.HasKernelAntiCheat).ToList();
        var eligible = targets.Count - protectedGames.Count;

        var body = $"将为 {targets.Count} 款游戏部署本项目：\n\n" +
                   string.Join("\n", targets.Select(t => "· " + t.Name)) +
                   "\n\n游戏必须处于完全退出状态。继续吗？";

        if (protectedGames.Count > 0)
        {
            body = $"将为 {eligible} 款游戏部署本项目：\n\n" +
                   string.Join("\n", targets.Where(t => !t.HasKernelAntiCheat).Select(t => "· " + t.Name)) +
                   $"\n\n以下 {protectedGames.Count} 款带有内核级反作弊，将被跳过：\n" +
                   string.Join("\n", protectedGames.Select(t => $"· {t.Name} — {t.Protection!.Products}")) +
                   "\n\n游戏必须处于完全退出状态。继续吗？";
        }

        if (MessageBox.Show(body, "全部部署", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        _log.Write($"— 批量部署 {targets.Count} 款游戏");
        var ok = 0;
        var blocked = 0;
        var source = CurrentSource();

        _busy = true;
        try
        {
            foreach (var game in targets)
            {
                // Batch mode never overrides the anti-cheat gate; protected games are listed instead.
                var result = DeploymentService.Deploy(game, source);
                var mark = result.Ok ? "✓" : "✗";
                _log.Write($"  {mark} {game.Name}：{result.Message}");

                if (result.Ok) ok++;
                else if (game.HasKernelAntiCheat) blocked++;
            }
        }
        finally
        {
            _busy = false;
        }

        _log.Write($"批量部署完成：成功 {ok} / {targets.Count}" +
                   (blocked > 0 ? $"，{blocked} 款因内核级反作弊被跳过" : ""));
        BatchStatusText.Text = $"上次批量部署：成功 {ok} / {targets.Count}";
        LibraryStore.Save(_data);
        RefreshAllStatus();
    }

    private void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var targets = _data.Games
            .Where(g => g.Deployment is not null && !string.IsNullOrWhiteSpace(g.RenderDir) && Directory.Exists(g.RenderDir))
            .ToList();

        if (targets.Count == 0) { _log.Write("没有已部署的游戏。"); return; }

        var body = $"将从 {targets.Count} 款游戏中移除本项目文件：\n\n" +
                   string.Join("\n", targets.Select(t => "· " + t.Name)) +
                   "\n\n只删除确认属于本项目的文件，被占用的原文件会还原。继续吗？";
        if (MessageBox.Show(body, "全部恢复", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        _log.Write($"— 批量恢复 {targets.Count} 款游戏");
        var ok = 0;
        var removeLogs = RemoveLogsCheck.IsChecked == true;

        _busy = true;
        try
        {
            foreach (var game in targets)
            {
                var result = DeploymentService.Restore(game, removeLogs);
                var mark = result.Ok ? "✓" : "✗";
                _log.Write($"  {mark} {game.Name}：{result.Message}");
                if (result.Ok) ok++;
            }
        }
        finally
        {
            _busy = false;
        }

        _log.Write($"批量恢复完成：成功 {ok} / {targets.Count}");
        BatchStatusText.Text = $"上次批量恢复：成功 {ok} / {targets.Count}";
        LibraryStore.Save(_data);
        RefreshAllStatus();
    }

    // ---- scanning -----------------------------------------------------------

    private void ScanSteam_Click(object sender, RoutedEventArgs e)
    {
        var progress = UiProgress();
        StartScan("Steam 库", token => Detection.ScanSteam(progress, token));
    }

    private void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择要扫描的文件夹（例如某个游戏盘）" };
        if (!string.IsNullOrWhiteSpace(_data.LastScanRoot) && Directory.Exists(_data.LastScanRoot))
            dialog.InitialDirectory = _data.LastScanRoot;

        if (dialog.ShowDialog() != true) return;

        var root = dialog.FolderName;
        _data.LastScanRoot = root;
        LibraryStore.Save(_data);

        var progress = UiProgress();
        StartScan(root, token => Detection.ScanFolder(root, progress, token));
    }

    /// <summary>
    /// Progress sink for the scan workers. Must be created on the UI thread: Progress&lt;T&gt; captures
    /// the SynchronizationContext it is constructed on, so building it inside a background lambda
    /// would run the callback on a pool thread and touching a control there kills the process.
    /// The dispatcher check keeps the sink correct even if that ever changes.
    /// </summary>
    private IProgress<string> UiProgress() => new Progress<string>(text =>
    {
        if (Dispatcher.CheckAccess()) BatchStatusText.Text = text;
        else Dispatcher.Invoke(() => BatchStatusText.Text = text);
    });

    private void StartScan(string label, Func<CancellationToken, List<GameCandidate>> scan)
    {
        if (_busy) { _log.Write("有操作正在进行，请稍候。"); return; }

        _busy = true;
        _scanCts = new CancellationTokenSource();
        _log.Write($"— 开始扫描：{label}");

        var token = _scanCts.Token;
        Task.Run(() =>
        {
            try { return scan(token); }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                AppPaths.Log("扫描失败: " + ex);
                return null;
            }
        }).ContinueWith(t =>
        {
            _busy = false;
            _scanCts?.Dispose();
            _scanCts = null;
            BatchStatusText.Text = "";

            var found = t.Result;
            if (found is null) { _log.Write("扫描已取消或失败。"); return; }
            MergeCandidates(found);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void MergeCandidates(List<GameCandidate> found)
    {
        var added = 0;
        var newlyAdded = new List<GameEntry>();

        foreach (var candidate in found)
        {
            var existing = _data.Games.FirstOrDefault(g =>
                string.Equals(g.RenderDir.TrimEnd('\\'), candidate.RenderDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(candidate.ExePath)) existing.ExePath = candidate.ExePath;
                DeploymentService.Check(existing);
                continue;
            }

            var game = new GameEntry
            {
                Name = candidate.Name,
                RenderDir = candidate.RenderDir,
                ExePath = candidate.ExePath,
                PreferredProxy = DeploymentService.AutoProxy,
                Notes = candidate.Source,
                Profile = new GameProfile { Router = _data.RecommendedRouter },
            };

            DeploymentService.Check(game);
            _data.Games.Add(game);
            newlyAdded.Add(game);
            added++;
        }

        LibraryStore.Save(_data);
        _log.Write($"扫描完成：发现 {found.Count} 个候选，新增 {added} 款。");

        // Land on something useful instead of leaving the detail pane empty after a scan.
        if (GameList.SelectedItem is null && _data.Games.Count > 0)
            GameList.SelectedIndex = 0;

        // One summary prompt for the whole scan rather than a dialog per protected title.
        WarnAboutProtected(newlyAdded);
    }

    // ---- path pickers -------------------------------------------------------

    private void BrowseRenderDir_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var dialog = new OpenFolderDialog { Title = "选择包含游戏渲染 EXE 的目录" };
        if (!string.IsNullOrWhiteSpace(game.RenderDir) && Directory.Exists(game.RenderDir))
            dialog.InitialDirectory = game.RenderDir;

        if (dialog.ShowDialog() != true) return;

        // AttachFolder resolves the render directory and raises the anti-cheat warning.
        AttachFolder(game, dialog.FolderName);
    }

    private void DetectRenderDir_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var root = game.RenderDir;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            _log.Write("请先「浏览…」选一个存在的目录，再自动定位。");
            return;
        }

        _log.Write($"— 在 {root} 中查找渲染目录");
        var hit = Detection.FindRenderTarget(root);
        if (hit is null)
        {
            _log.Write("未找到 nvngx_dlssg.dll。该游戏可能不支持 DLSS 帧生成。");
            return;
        }

        _log.Write($"已定位：{hit.RenderDir}");
        AttachFolder(game, root);
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var dialog = new OpenFileDialog { Title = "选择游戏主程序", Filter = "可执行文件 (*.exe)|*.exe" };
        if (!string.IsNullOrWhiteSpace(game.ExePath) && File.Exists(game.ExePath))
            dialog.InitialDirectory = Path.GetDirectoryName(game.ExePath);

        if (dialog.ShowDialog() != true) return;

        game.ExePath = dialog.FileName;
        LibraryStore.Save(_data);
    }

    // ---- open / launch ------------------------------------------------------

    private void OpenRenderDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = Selected?.RenderDir;
        if (!Shell.OpenFolder(dir)) _log.Write("目录不存在或无法打开。");
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        if (Shell.LaunchExecutable(game.ExePath))
            _log.Write("已启动 " + Path.GetFileName(game.ExePath));
        else
            _log.Write("启动失败：请先设置有效的启动程序路径。");
    }

    private void OpenModLog_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var latest = DeploymentService.LatestLogFile(game.RenderDir);
        if (latest is not null)
        {
            Shell.OpenDocument(latest);
            _log.Write("已打开最新日志：" + Path.GetFileName(latest));
            return;
        }

        var logsDir = Path.Combine(game.RenderDir, ModSource.LogDirName, "logs");
        if (Directory.Exists(logsDir))
        {
            Shell.OpenFolder(logsDir);
            return;
        }

        _log.Write("还没有 Mod 日志。把该游戏的日志级别设为 2，启动游戏后即可生成。");
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        if (!Shell.OpenFolder(AppPaths.Root)) _log.Write("无法打开数据目录：" + AppPaths.Root);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (Native.IsElevated()) return;

        if (Shell.RelaunchElevated())
            Application.Current.Shutdown();
        else
            _log.Write("提权被取消或失败。也可以右键 exe 选择「以管理员身份运行」。");
    }
}
