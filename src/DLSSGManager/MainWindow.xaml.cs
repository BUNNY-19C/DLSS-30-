using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DLSSGManager;

public partial class MainWindow : Window
{
    private readonly AppData _data;
    private OutputLog _log = null!;
    private CancellationTokenSource? _scanCts;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.EnsureCreated();
        _data = LibraryStore.Load();

        _log = new OutputLog(OutputBox);

        FrameCombo.ItemsSource = new[]
        {
            new Choice(1, "2X（最多额外 1 帧）"),
            new Choice(2, "3X（最多额外 2 帧）"),
            new Choice(3, "4X（最多额外 3 帧）"),
        };

        LogCombo.ItemsSource = new[]
        {
            new Choice(0, "0 · 关闭"),
            new Choice(1, "1 · 仅错误"),
            new Choice(2, "2 · 运行诊断"),
            new Choice(3, "3 · 详细日志"),
        };

        // Bound straight to the persisted collection: no copy can drift out of sync with the file.
        GameList.ItemsSource = _data.Games;
        if (_data.Games.Count > 0) GameList.SelectedIndex = 0;

        Loaded += OnLoaded;
    }

    private GameEntry? Selected => GameList.SelectedItem as GameEntry;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AdminButton.Content = Native.IsElevated() ? "已是管理员" : "以管理员身份重启";
        AdminButton.IsEnabled = !Native.IsElevated();

        RefreshModSource();
        RefreshAllStatus();

        _log.Write($"数据目录：{AppPaths.Root}");
        if (!Native.IsElevated())
            _log.Write("提示：游戏若装在 Program Files 下，写入需要管理员权限，可用右上角按钮重启。");

        ProbeGpuInBackground();
    }

    private void ProbeGpuInBackground()
    {
        var ui = TaskScheduler.FromCurrentSynchronizationContext();

        Task.Run(Gpu.Probe).ContinueWith(t =>
        {
            var info = t.Result;
            _data.GpuName = info.Name;
            _data.GpuDriver = info.Driver;
            _data.RecommendedRouter = info.Router;
            LibraryStore.Save(_data);

            GpuText.Text = string.IsNullOrWhiteSpace(info.Driver)
                ? info.Name
                : $"{info.Name}（驱动 {info.Driver}）";
            RouterHintText.Text = info.Advice;
            _log.Write($"显卡：{info.Name} · {info.Advice}");
        }, ui);
    }

    // ---- mod source ---------------------------------------------------------

    /// <summary>
    /// Where mod files are read from. Resolved through ModSourceLocator so a source checkout run from
    /// bin\ still finds the repository's mod\ folder.
    /// </summary>
    private string SourcePath =>
        ModSourceLocator.FindExisting(_data.ModSourcePath) ?? _data.ModSourcePath;

    private ModSource CurrentSource() => new(SourcePath);

    private bool HasModSource => ModSourceLocator.FindExisting(_data.ModSourcePath) is not null;

    private void RefreshModSource()
    {
        var existing = ModSourceLocator.FindExisting(_data.ModSourcePath);

        if (existing is null)
        {
            // Show where a download would land, and say plainly that files are missing.
            var target = ModSourceLocator.ResolveTarget(_data.ModSourcePath);
            ModSourceText.Text = target;
            ModSourceBadgeText.Text = "未就绪 · 请先获取 Mod 文件";
            ModSourceBadge.Background = Palette.Fill(Palette.Warn);
            ModSourceText.ToolTip = "缺少 Mod 文件。点右侧「从 GitHub 更新 Mod 文件」自动获取，详见 docs/mod-files.md。";
            _log.Write("Mod 文件源未就绪：尚未获取 mod 文件。点「从 GitHub 更新 Mod 文件」自动下载。");
            return;
        }

        var source = new ModSource(existing);
        ModSourceText.Text = existing;
        ModSourceBadgeText.Text = source.IsValid ? $"可用 · Native {source.Version}" : "文件不完整";
        ModSourceBadge.Background = Palette.Fill(source.IsValid ? Palette.Ok : Palette.Bad);
        ModSourceText.ToolTip = source.IsValid
            ? "代理入口：" + string.Join("、", source.Proxies)
            : source.ValidationMessage;

        if (!source.IsValid) _log.Write("Mod 文件源不完整：" + source.ValidationMessage);
    }

    private void UpdateMod_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { _log.Write("有操作正在进行，请稍候。"); return; }

        var target = ModSourceLocator.ResolveTarget(_data.ModSourcePath);
        var answer = MessageBox.Show(
            "将从 GitHub 下载 dlssg_for_sm86 的最新源码包，解压并写入 Mod 文件目录：\n\n" +
            target + "\n\n下载源限定为 github.com / codeload.github.com（HTTPS）。继续吗？",
            "更新 Mod 文件", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK) return;

        _busy = true;
        _log.Write("— 从 GitHub 更新 Mod 文件");

        var progress = UiProgress();

        Task.Run(() => ModFetcher.DownloadIntoAsync(target, progress, CancellationToken.None))
            .ContinueWith(t =>
            {
                _busy = false;
                var result = t.Result;
                _log.Details(result.Lines);
                _log.Result(result.Ok, result.Message);
                BatchStatusText.Text = "";

                // Re-read the profile defaults from the freshly downloaded INI text.
                RefreshModSource();
                if (result.Ok)
                {
                    MessageBox.Show(
                        result.Message + "\n\n如果 Mod 有新的配置项，可在各游戏的配置面板里重新部署以写入。",
                        "更新完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- game list ----------------------------------------------------------

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var game = Selected;
        NoSelectionText.Visibility = game is null ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = game is null ? Visibility.Collapsed : Visibility.Visible;
        if (game is null) return;

        DataContext = game;
        ProxyCombo.SelectedValue = game.PreferredProxy;
        UpdateStatusCard();
    }

    private void ProxyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is null) return;
        if (ProxyCombo.SelectedValue is string value) Selected.PreferredProxy = value;
    }

    private void UpdateStatusCard()
    {
        var game = Selected;
        if (game is null) return;

        StatusTitle.Text = game.StatusText + " — " + game.StatusDetail;
        StatusTitle.Foreground = Palette.Fill(game.StatusColor);
        StatusBody.Text = StatusDetailText(game);

        var deployed = game.Deployment is not null;
        RestoreButton.IsEnabled = deployed;
        AdoptButton.IsEnabled = !deployed || game.Status == GameStatus.Modified;
        OpenLogButton.IsEnabled = game.RenderDir is not null &&
                                  Directory.Exists(Path.Combine(game.RenderDir, ModSource.LogDirName));

        // A kernel anti-cheat makes the mod impossible to use there, so the action is disabled rather
        // than merely warned about. Restore stays available to clean up a deployment made earlier.
        DeployButton.IsEnabled = !game.HasKernelAntiCheat;
        DeployButton.ToolTip = game.HasKernelAntiCheat
            ? "该游戏带有内核级反作弊，本 Mod 无法在其上生效"
            : null;
    }

    private static string StatusDetailText(GameEntry game)
    {
        if (game.Deployment is null)
            return "部署后本管理器会记录文件指纹，恢复时只删除确认属于本项目的文件；被占用的原文件会先备份。";

        var parts = new List<string>
        {
            "入口 " + game.Deployment.ProxyName,
            "Mod " + game.Deployment.ModVersion,
            "部署于 " + game.Deployment.DeployedAt,
        };

        if (game.Deployment.Backups.Count > 0)
            parts.Add($"已备份 {game.Deployment.Backups.Count} 个原文件");

        return string.Join(" · ", parts);
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择游戏文件夹（包含游戏主程序的目录）" };
        if (!string.IsNullOrWhiteSpace(_data.LastScanRoot) && Directory.Exists(_data.LastScanRoot))
            dialog.InitialDirectory = _data.LastScanRoot;
        if (dialog.ShowDialog() != true) return;

        var folder = dialog.FolderName;
        _data.LastScanRoot = folder;

        var game = new GameEntry
        {
            Name = Detection.FriendlyName(folder),
            PreferredProxy = DeploymentService.AutoProxy,
            Profile = new GameProfile { Router = _data.RecommendedRouter },
        };

        _data.Games.Add(game);
        GameList.SelectedItem = game;
        _log.Write($"— 添加游戏：{folder}");

        // AttachFolder fills in the render directory, runs the anti-cheat scan and warns if needed.
        AttachFolder(game, folder);
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var body = $"从列表移除「{game.Name}」？";
        if (game.Deployment is not null)
            body += "\n\n注意：该游戏仍处于部署状态。移除条目不会删除游戏目录里的文件；请先执行「一键恢复」。";

        if (MessageBox.Show(body, "移除", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        _data.Games.Remove(game);
        LibraryStore.Save(_data);
        GameList.SelectedIndex = _data.Games.Count > 0 ? 0 : -1;
    }

    // ---- status -------------------------------------------------------------

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        DeploymentService.Check(game);
        UpdateStatusCard();
        _log.Write($"检查 {game.Name}：{game.StatusText} — {game.StatusDetail}");
    }

    private void RefreshAll_Click(object sender, RoutedEventArgs e) => RefreshAllStatus();

    private void RefreshAllStatus()
    {
        foreach (var game in _data.Games) DeploymentService.Check(game);
        LibraryStore.Save(_data);
        UpdateStatusCard();
    }
}
