using System.Windows;
using System.Windows.Media;

namespace DLSSGManager;

/// <summary>
/// Swaps the application's colour resources at runtime.
///
/// Every colour in the interface is a <c>DynamicResource</c> lookup into the dictionary installed
/// here, so switching replaces the brushes and WPF re-resolves them — no window rebuild, and open
/// dialogs follow along because the lookup goes through the application-level dictionary.
///
/// Dynamic rather than static is the load-bearing choice: a <c>StaticResource</c> is resolved once
/// when the element is created, so a theme switch would leave most of the window in the old colours.
/// </summary>
public static class Theme
{
    private const string DarkSource = "Themes/Dark.xaml";
    private const string LightSource = "Themes/Light.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        var uri = new Uri(theme == AppTheme.Light ? LightSource : DarkSource, UriKind.Relative);

        // The theme lives at index 0; anything appended later stays after it.
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = uri };

        if (dictionaries.Count == 0) dictionaries.Add(replacement);
        else dictionaries[0] = replacement;

        Current = theme;
        AppPaths.Log($"界面主题已切换为 {(theme == AppTheme.Light ? "浅色" : "深色")}");
    }

    public static void Apply(string? stored) => Apply(ThemeKeys.Parse(stored));

    /// <summary>
    /// Reads a brush from the active theme by key. Used by code that assigns colours directly, which
    /// cannot participate in the resource lookup — note that such code must re-run after a theme
    /// switch, so the caller does that explicitly.
    /// </summary>
    public static Brush Brush(string key)
    {
        var found = Application.Current?.TryFindResource(key);
        return found as Brush ?? Brushes.Transparent;
    }
}
