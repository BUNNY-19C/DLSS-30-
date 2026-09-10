using System.Windows.Media;

namespace DLSSGManager;

/// <summary>Central place for the status colours used by both the list and the detail card.</summary>
public static class Palette
{
    public const string Ok = "#4C9A2A";
    public const string Warn = "#C77700";
    public const string Bad = "#C62828";
    public const string Idle = "#9AA0A6";

    public static SolidColorBrush Fill(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }
}
