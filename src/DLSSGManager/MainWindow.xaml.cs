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

    /// <summary>Keeps overlapping status refreshes from racing over the same game properties.</summary>
    private bool _statusRefreshRunning;

    /// <summary>Prevents the language handler firing while the picker is being populated.</summary>
    private bool _suppressLanguageChange;

    /// <summary>
    /// Last GPU probe result, kept so the advice line can be re-rendered after a language change.
    /// The text is produced from code, so it does not follow the XAML bindings.
    /// </summary>
    private GpuInfo? _gpuInfo;

    public MainWindow()
    {
        // The language must be applied before the first XAML string is resolved, otherwise the window
        // renders in the default language and only switches a moment later.
        Loc.SetLanguage(LibraryStore.Load().InterfaceLanguage);

        InitializeComponent();

        AppPaths.EnsureCreated();
        _data = LibraryStore.Load();

        _log = new OutputLog(OutputBox);

        BuildLocalizedCombos();
        BuildLanguageCombo();

        // Bound straight to the persisted collection: no copy can drift out of sync with the file.
        GameList.ItemsSource = _data.Games;
        if (_data.Games.Count > 0) GameList.SelectedIndex = 0;

        Loaded += OnLoaded;
    }

    private GameEntry? Selected => GameList.SelectedItem as GameEntry;

    /// <summary>
    /// Fills the numeric dropdowns from the string table. Called again after a language change, since
    /// these items carry display text rather than a binding.
    /// </summary>
    private void BuildLocalizedCombos()
    {
        var frames = FrameCombo.SelectedValue;
        FrameCombo.ItemsSource = new[]
        {
            new Choice(1, Loc.T("Detail.Frames2X")),
            new Choice(2, Loc.T("Detail.Frames3X")),
            new Choice(3, Loc.T("Detail.Frames4X")),
        };
        if (frames is not null) FrameCombo.SelectedValue = frames;

        var level = LogCombo.SelectedValue;
        LogCombo.ItemsSource = new[]
        {
            new Choice(0, Loc.T("Detail.Log0")),
            new Choice(1, Loc.T("Detail.Log1")),
            new Choice(2, Loc.T("Detail.Log2")),
            new Choice(3, Loc.T("Detail.Log3")),
        };
        if (level is not null) LogCombo.SelectedValue = level;
    }

    /// <summary>Fills the language picker without triggering the change handler.</summary>
    private void BuildLanguageCombo()
    {
        _suppressLanguageChange = true;
        LanguageCombo.ItemsSource = Languages.All
            .Select(code => new TextChoice(code, Languages.DisplayName(code)))
            .ToList();
        LanguageCombo.SelectedValuePath = "Value";
        LanguageCombo.DisplayMemberPath = "Text";
        LanguageCombo.SelectedValue = Loc.Current;
        _suppressLanguageChange = false;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;
        if (LanguageCombo.SelectedItem is not TextChoice choice) return;

        var language = choice.Value;

        // WPF raises SelectionChanged again while the dropdown's item containers are realised, and
        // that second event is not distinguishable from a user pick. Without this guard the handler
        // would run again with the same value, and — because it used to act unconditionally — it also
        // persisted the result, so a startup-time spurious event silently overwrote the saved
        // language. Acting only on a genuine change makes repeated events harmless.
        if (string.Equals(language, Loc.Current, StringComparison.Ordinal)) return;

        Loc.SetLanguage(language);

        // Text set from code does not follow the bindings, so it is re-applied here.
        BuildLocalizedCombos();
        RefreshCodeText();
        RefreshModSource();
        UpdateStatusCard();
        RefreshAllStatus();

        _data.InterfaceLanguage = language;
        LibraryStore.Save(_data);
    }

    /// <summary>
    /// Re-applies the interface text that is assigned from code rather than bound in XAML, so a
    /// language change does not leave part of the window in the previous language.
    /// </summary>
    private void RefreshCodeText()
    {
        AdminButton.Content = Loc.T(Native.IsElevated() ? "Toolbar.AlreadyAdmin" : "Toolbar.RestartAdmin");

        // The game rows bind to computed properties on GameEntry, which the language change cannot
        // reach on its own — see RaiseLocalizedText.
        foreach (var game in _data.Games) game.RaiseLocalizedText();

        ApplyGpuText();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AdminButton.IsEnabled = !Native.IsElevated();
        RefreshCodeText();

        RefreshModSource();
        RefreshAllStatus();

        _log.Write(Loc.T("Status.DataDir", AppPaths.Root));
        if (!Native.IsElevated())
            _log.Write(Loc.T("Status.NoPermissionHint"));

        ProbeGpuInBackground();

        // A freshly downloaded exe has no mod files yet, and the download button is easy to miss.
        // Offer the download up front so "download and run" is the whole setup.
        if (!HasModSource) OfferFirstRunDownload();
    }

    /// <summary>
    /// Asks, once, whether to fetch the mod files now. Declining leaves the app usable — the status
    /// banner keeps pointing at the button.
    /// </summary>
    private void OfferFirstRunDownload()
    {
        var target = ModSourceLocator.ResolveTarget(_data.ModSourcePath);

        var body =
            Loc.T("Fetch.FirstRunBody", target);

        var answer = MessageBox.Show(this, body, Loc.T("Fetch.FirstRunTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);

        if (answer == MessageBoxResult.Yes)
        {
            DownloadModFiles(target, update: false);
        }
        else
        {
            _log.Write(Loc.T("Fetch.SkipLog"));
        }
    }

    private void ProbeGpuInBackground()
    {
        var ui = TaskScheduler.FromCurrentSynchronizationContext();

        Task.Run(Gpu.Probe).ContinueWith(t =>
        {
            var info = t.Result;
            _gpuInfo = info;

            _data.GpuName = info.Name;
            _data.GpuDriver = info.Driver;
            _data.RecommendedRouter = info.Router;
            LibraryStore.Save(_data);

            ApplyGpuText();
            _log.Write(Loc.T("Toolbar.Gpu") + " " + info.Name + " · " + info.Advice);
        }, ui);
    }

    /// <summary>
    /// Renders the GPU lines. Called both when the probe finishes and after a language change, since
    /// the advice text is built in code and would otherwise stay in the previous language.
    /// </summary>
    private void ApplyGpuText()
    {
        if (_gpuInfo is null) return;

        GpuText.Text = string.IsNullOrWhiteSpace(_gpuInfo.Driver)
            ? _gpuInfo.Name
            : Loc.T("Gpu.NameWithDriver", _gpuInfo.Name, _gpuInfo.Driver);

        // Regenerated rather than reused: the advice is assembled in code, so the stored string from
        // the probe is in whatever language was active at probe time.
        RouterHintText.Text = Gpu.AdviceFor(_gpuInfo);
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
            ModSourceBadgeText.Text = Loc.T("Toolbar.ModNotReady");
            ModSourceBadge.Background = Palette.Fill(Palette.Warn);
            ModSourceText.ToolTip = Loc.T("Toolbar.ModMissingTip");
            _log.Write(Loc.T("Fetch.NotReadyLog"));
            return;
        }

        var source = new ModSource(existing);
        ModSourceText.Text = existing;
        ModSourceBadgeText.Text = source.IsValid ? Loc.T("Toolbar.ModReady", source.Version) : Loc.T("Toolbar.ModIncomplete");
        ModSourceBadge.Background = Palette.Fill(source.IsValid ? Palette.Ok : Palette.Bad);
        ModSourceText.ToolTip = source.IsValid
            ? Loc.T("Toolbar.ModReadyTip", Loc.Join(source.Proxies))
            : source.ValidationMessage;

        if (!source.IsValid) _log.Write(Loc.T("Fetch.IncompleteLog", source.ValidationMessage));
    }

    private void UpdateMod_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { _log.Write(Loc.T("Scan.Busy")); return; }

        var target = ModSourceLocator.ResolveTarget(_data.ModSourcePath);
        var answer = MessageBox.Show(this,
            Loc.T("Fetch.Confirm", target),
            Loc.T("Fetch.ConfirmTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK) return;

        DownloadModFiles(target, update: true);
    }

    /// <summary>
    /// Downloads the mod files into <paramref name="target"/>. Shared by the toolbar button and the
    /// first-run prompt so both behave identically.
    /// </summary>
    private void DownloadModFiles(string target, bool update)
    {
        if (_busy) { _log.Write(Loc.T("Scan.Busy")); return; }

        _busy = true;
        _log.Write(update ? Loc.T("Fetch.StartLog") : Loc.T("Fetch.StartLogFirst"));

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
                    MessageBox.Show(this,
                        result.Message + (update
                            ? Loc.T("Fetch.DoneBody", result.Message)
                            : Loc.T("Fetch.DoneBodyFirst", result.Message)),
                        update ? Loc.T("Fetch.DoneTitle") : Loc.T("Fetch.DoneTitleFirst"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
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
            ? Loc.T("Deploy.BlockedTooltip")
            : null;
    }

    private static string StatusDetailText(GameEntry game)
    {
        if (game.Deployment is null)
            return Loc.T("Status.DeployHint");

        var parts = new List<string>
        {
            game.Deployment.ProxyName,
            "Mod " + game.Deployment.ModVersion,
            game.Deployment.DeployedAt,
        };

        if (game.Deployment.Backups.Count > 0)
            parts.Add(Loc.T("Detail.BackupCount", game.Deployment.Backups.Count));

        return string.Join(" · ", parts);
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("List.AddFolderTitle") };
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
        _log.Write(Loc.T("List.AddedMessage", folder));

        // AttachFolder fills in the render directory, runs the anti-cheat scan and warns if needed.
        AttachFolder(game, folder);
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var body = Loc.T("List.RemoveConfirm", game.Name);
        if (game.Deployment is not null)
            body += Loc.T("List.RemoveWarnDeployed");

        if (MessageBox.Show(body, Loc.T("List.RemoveTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        _data.Games.Remove(game);
        LibraryStore.Save(_data);
        GameList.SelectedIndex = _data.Games.Count > 0 ? 0 : -1;
    }

    // ---- status -------------------------------------------------------------

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var game = Selected;
        if (game is null) return;

        var check = await Task.Run(() => DeploymentService.Evaluate(game));
        DeploymentService.Apply(game, check);

        UpdateStatusCard();
        _log.Write(Loc.T("Status.CheckResult", game.Name, game.StatusText, game.StatusDetail));
    }

    private void RefreshAll_Click(object sender, RoutedEventArgs e) => RefreshAllStatus();

    /// <summary>
    /// Re-reads every game's state.
    ///
    /// The inspection runs on the thread pool because it is expensive: each candidate entry name is
    /// verified with WinVerifyTrust over a ~15 MB DLL, and a deployed game is hashed again. Doing
    /// that inline froze the window for seconds once a few games were listed. Results are applied
    /// back here, since assigning those properties is what raises the change notifications the list
    /// binds to.
    /// </summary>
    private async void RefreshAllStatus()
    {
        // Called at startup, after batch operations, and by the refresh button, so two runs can
        // otherwise overlap and fight over the same properties.
        if (_statusRefreshRunning) return;
        _statusRefreshRunning = true;

        try
        {
            var games = _data.Games.ToList();
            var results = await Task.Run(() =>
                games.Select(g => (Game: g, Check: DeploymentService.Evaluate(g))).ToList());

            foreach (var (game, check) in results) DeploymentService.Apply(game, check);

            LibraryStore.Save(_data);
            UpdateStatusCard();
        }
        catch (Exception ex)
        {
            _log.Write(Loc.T("Status.RefreshFailed", ex.Message));
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }
}
