using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DLSSGManager;

public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

public sealed class NotEmptyToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Resolves a theme resource key to the brush it names.
///
/// Used where a model exposes a status colour as a key — a binding cannot look up a resource itself,
/// and a bound Brush would be captured once and go stale on a theme switch.
/// </summary>
public sealed class ThemeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Theme.Brush(key) : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A combo entry pairing a stable value with the label shown in the dropdown.</summary>
public sealed record Choice(int Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// The same pairing for string values, used by the language picker where the value is a language code
/// rather than a number. Kept separate so the numeric combos keep their compile-time type.
/// </summary>
public sealed record TextChoice(string Value, string Text)
{
    public override string ToString() => Text;
}
