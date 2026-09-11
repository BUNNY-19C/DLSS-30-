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
            _log.Write("✗ " + Loc.T("Deploy.NeedDir"));
            return;
        }

        RunDeploy(game);
    }

    private void RunDeploy(GameEntry game)
    {
        if (_busy) { _log.Write(Loc.T("Scan.Busy")); return; }

        var allowProtected = false;
        var protection = AntiCheat.Scan(game.RenderDir);
        game.Protection = protection;

        if (protection.HasKernelAntiCheat)
        {
            var body = Loc.T("Anti.OverrideBody", game.Name, protection.Products, protection.Evidence);

            var answer = MessageBox.Show(body, Loc.T("Anti.OverrideTitle"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                _log.Write(Loc.T("Anti.CancelLog", game.Name, protection.Summary, protection.Evidence));
                return;
            }

            allowProtected = true;
            _log.Write(Loc.T("Anti.OverrideLog", game.Name, protection.Summary));
        }

        _busy = true;
        try
        {
            _log.Write(Loc.T("Deploy.Starting", game.Name));
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
    /// showing the status from before the operation (Loc.T("Status.Missing") after a successful restore, etc.).
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
            var body = Loc.T("Restore.NoRecord", game.Name);
            if (MessageBox.Show(body, Loc.T("Restore.NoRecordTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }

        RunRestore(game);
    }

    private void RunRestore(GameEntry game)
    {
        if (_busy) { _log.Write(Loc.T("Scan.Busy")); return; }

        _busy = true;
        try
        {
            _log.Write(Loc.T("Restore.Starting", game.Name));
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

        var body = Loc.T("Adopt.Confirm");
        if (MessageBox.Show(body, Loc.T("Adopt.ConfirmTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
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

        if (targets.Count == 0) { _log.Write(Loc.T("Batch.NothingToDeploy")); return; }

        var protectedGames = targets.Where(g => g.HasKernelAntiCheat).ToList();
        var eligible = targets.Count - protectedGames.Count;

        var body = Loc.T("Batch.DeployConfirm", targets.Count,
            string.Join("\n", targets.Select(t => "· " + t.Name)));

        if (protectedGames.Count > 0)
        {
            body = Loc.T("Batch.DeployConfirmWithSkipped",
                eligible,
                string.Join("\n", targets.Where(t => !t.HasKernelAntiCheat).Select(t => "· " + t.Name)),
                protectedGames.Count,
                string.Join("\n", protectedGames.Select(t => $"· {t.Name} — {t.Protection!.Products}")));
        }

        if (MessageBox.Show(body, Loc.T("Batch.DeployTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        _log.Write(Loc.T("Batch.DeployStart", targets.Count));
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

        _log.Write(Loc.T("Batch.Result", Loc.T("Batch.Deploy"), ok, targets.Count) +
                   (blocked > 0 ? Loc.T("Batch.ResultSkipped", blocked) : ""));
        BatchStatusText.Text = Loc.T("Batch.LastDeploy", ok, targets.Count);
        LibraryStore.Save(_data);
        RefreshAllStatus();
    }

    private void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var targets = _data.Games
            .Where(g => g.Deployment is not null && !string.IsNullOrWhiteSpace(g.RenderDir) && Directory.Exists(g.RenderDir))
            .ToList();

        if (targets.Count == 0) { _log.Write(Loc.T("Batch.NothingToRestore")); return; }

        var body = Loc.T("Batch.RestoreConfirm", targets.Count,
            string.Join("\n", targets.Select(t => "· " + t.Name)));
        if (MessageBox.Show(body, Loc.T("Batch.RestoreTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        _log.Write(Loc.T("Batch.RestoreStart", targets.Count));
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

        _log.Write(Loc.T("Batch.Result", Loc.T("Batch.Restore"), ok, targets.Count));
        BatchStatusText.Text = Loc.T("Batch.LastRestore", ok, targets.Count);
        LibraryStore.Save(_data);
        RefreshAllStatus();
    }

    // ---- scanning -----------------------------------------------------------

    private void ScanSteam_Click(object sender, RoutedEventArgs e)
    {
        var progress = UiProgress();
        StartScan(Loc.T("Scan.SteamLabel"), token => Detection.ScanSteam(progress, token));
    }

    private void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Scan.FolderTitle") };
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
        if (_busy) { _log.Write(Loc.T("Scan.Busy")); return; }

        _busy = true;
        _scanCts = new CancellationTokenSource();
        _log.Write(Loc.T("Scan.Start", label));

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
            if (found is null) { _log.Write(Loc.T("Scan.Cancelled")); return; }
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
        _log.Write(Loc.T("Scan.Done", found.Count, added));

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

        var dialog = new OpenFolderDialog { Title = Loc.T("Detail.BrowseDirTitle") };
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
            _log.Write(Loc.T("Detail.DetectNeedFolder"));
            return;
        }

        _log.Write(Loc.T("Detail.DetectSearching", root));
        var hit = Detection.FindRenderTarget(root);
        if (hit is null)
        {
            _log.Write(Loc.T("Detail.DetectNoMarker"));
            return;
        }

        _log.Write(Loc.T("Detail.LocatedMessage", hit.RenderDir));
        AttachFolder(game, root);
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var dialog = new OpenFileDialog { Title = Loc.T("Detail.BrowseExeTitle"), Filter = Loc.T("Detail.ExeFilter") };
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
        if (!Shell.OpenFolder(dir)) _log.Write(Loc.T("Error.DirOpenFailed"));
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        if (Shell.LaunchExecutable(game.ExePath))
            _log.Write(Loc.T("Error.Launched", Path.GetFileName(game.ExePath)));
        else
            _log.Write(Loc.T("Error.LaunchFailed"));
    }

    private void OpenModLog_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var latest = DeploymentService.LatestLogFile(game.RenderDir);
        if (latest is not null)
        {
            Shell.OpenDocument(latest);
            _log.Write(Loc.T("Error.LogOpened", Path.GetFileName(latest)));
            return;
        }

        var logsDir = Path.Combine(game.RenderDir, ModSource.LogDirName, "logs");
        if (Directory.Exists(logsDir))
        {
            Shell.OpenFolder(logsDir);
            return;
        }

        _log.Write(Loc.T("Error.NoModLog"));
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        if (!Shell.OpenFolder(AppPaths.Root)) _log.Write(Loc.T("Error.CannotOpenDataDir", AppPaths.Root));
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (Native.IsElevated()) return;

        if (Shell.RelaunchElevated())
            Application.Current.Shutdown();
        else
            _log.Write(Loc.T("Error.ElevationCancelled"));
    }
}
