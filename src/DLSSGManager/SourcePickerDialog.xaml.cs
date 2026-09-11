using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DLSSGManager;

/// <summary>
/// Lets the user pick where to download the mod files from.
///
/// Rows are built in code rather than declared in XAML because the list comes from
/// <see cref="ModFetcher.AvailableSources"/>: adding a source to the fetcher makes it selectable here
/// without touching this file.
///
/// "Automatic" is offered first and pre-selected — trying each source in turn is what most users
/// want. Picking one source deliberately is for when an endpoint is known to be needed, or is being
/// diagnosed, and in that case the downloader does not silently fall back to another.
/// </summary>
public partial class SourcePickerDialog : Window
{
    /// <summary>Selected source id, or <see cref="ModFetcher.AutoSourceId"/> for "try each in turn".</summary>
    public string SelectedSourceId { get; private set; } = ModFetcher.AutoSourceId;

    public SourcePickerDialog()
    {
        InitializeComponent();
        BuildList();
    }

    private void BuildList()
    {
        // Automatic first.
        OptionsPanel.Children.Add(BuildOption(
            ModFetcher.AutoSourceId,
            Loc.T("Fetch.PickerAuto"),
            badge: null,
            Loc.T("Fetch.PickerAutoNote"),
            isChecked: true));

        OptionsPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x33, 0x3D)),
            Margin = new Thickness(0, 8, 0, 8),
        });

        foreach (var source in ModFetcher.AvailableSources)
        {
            OptionsPanel.Children.Add(BuildOption(
                source.Id,
                source.Name,
                source.Official ? Loc.T("Fetch.PickerOfficial") : Loc.T("Fetch.PickerMirror"),
                source.Note,
                isChecked: false));
        }
    }

    private RadioButton BuildOption(string id, string title, string? badge, string note, bool isChecked)
    {
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });

        if (badge is not null)
        {
            // Marks an official endpoint versus a third-party mirror. The difference matters: on a
            // mirror the certificate pin is enforced strictly, because the mirror is not the authority
            // for the content.
            titleRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badge,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                },
            });
        }

        var content = new StackPanel();
        content.Children.Add(titleRow);
        content.Children.Add(new TextBlock
        {
            Text = note,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA1)),
        });

        var radio = new RadioButton
        {
            GroupName = "source",
            Tag = id,
            IsChecked = isChecked,
            Content = content,
            Margin = new Thickness(0, 4, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        radio.Checked += (_, _) => SelectedSourceId = id;
        return radio;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
