using System.IO;
using System.Windows;

namespace DLSSGManager;

/// <summary>
/// Anti-cheat warnings raised at the moment a folder is attached to a game, rather than only when a
/// deploy is attempted. A user adding a protected game should learn immediately that the mod cannot
/// work there, not after they have copied files into it.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Points a game at a folder, resolving the folder down to the real render directory when needed,
    /// then reports whether anti-cheat will block the mod there.
    ///
    /// Resolution matters for the scan: anti-cheat files sit beside the rendering executable, so
    /// pointing at a game root (Overwatch's "E:\Overwatch" instead of its "_retail_" folder) would
    /// otherwise walk up past them and miss the driver entirely.
    /// </summary>
    private void AttachFolder(GameEntry game, string folder)
    {
        var (renderDir, exe) = Detection.ResolveRenderDir(folder);

        if (!string.Equals(Path.GetFullPath(renderDir), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            _log.Write($"  该目录不是渲染目录，已定位到 {renderDir}");

        game.RenderDir = renderDir;
        if (exe is not null) game.ExePath = exe;

        if (string.IsNullOrWhiteSpace(game.Name) || game.Name == "新游戏")
        {
            var friendly = Detection.FriendlyName(renderDir);
            if (!string.IsNullOrWhiteSpace(friendly)) game.Name = friendly;
        }

        DeploymentService.Check(game);
        LibraryStore.Save(_data);
        UpdateStatusCard();

        WarnIfProtected(game);
    }

    /// <summary>
    /// Scans the game's folder and, when a kernel-mode anti-cheat is present, explains the consequence.
    /// Returns true when a warning was shown.
    /// </summary>
    private bool WarnIfProtected(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.RenderDir) || !Directory.Exists(game.RenderDir)) return false;

        var protection = AntiCheat.Scan(game.RenderDir);
        game.Protection = protection;
        if (!protection.HasKernelAntiCheat) return false;

        var name = string.IsNullOrWhiteSpace(game.Name) ? Detection.FriendlyName(game.RenderDir) : game.Name;
        _log.Write($"⚠ {name}：{protection.Summary}（{protection.Evidence}）");

        MessageBox.Show(this,
            AntiCheat.BuildUnsupportedNotice(name, protection),
            "该游戏无法使用本 Mod",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return true;
    }

    /// <summary>
    /// One summary prompt for a batch of newly added games, so scanning a whole drive does not produce
    /// a popup per protected title.
    /// </summary>
    private void WarnAboutProtected(List<GameEntry> newlyAdded)
    {
        var blocked = newlyAdded.Where(g => g.HasKernelAntiCheat).ToList();
        if (blocked.Count == 0) return;

        var lines = blocked.Select(g => $"· {g.Name} — {g.Protection!.Products}");
        var body =
            $"新增的 {blocked.Count} 款游戏带有内核级反作弊，本 Mod 无法在这些游戏上生效：\n\n" +
            string.Join("\n", lines) +
            "\n\n它们会被标记为不可部署；如果游戏自带帧生成，请直接在游戏内开启。\n" +
            "在列表里选中任一游戏可以看到详细信息。";

        _log.Write($"⚠ 新增游戏中 {blocked.Count} 款带有内核级反作弊：" +
                   string.Join("、", blocked.Select(g => g.Name)));

        MessageBox.Show(this, body, "有游戏无法使用本 Mod",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
