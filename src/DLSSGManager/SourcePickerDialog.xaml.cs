using System.Windows;
using System.Windows.Controls;

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
///
/// All colours come from styles declared in the matching XAML file, which in turn reference the
/// application theme dictionary. Colours must not be set inline here: an inline brush is fixed at
/// creation time and would keep the dialog in the dark theme after a switch.
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

    private Style? LookupStyle(string key) => TryFindResource(key) as Style;

    private void BuildList()
    {
        // Automatic first.
        OptionsPanel.Children.Add(BuildOption(
            ModFetcher.AutoSourceId,
            Loc.T("Fetch.PickerAuto"),
            badge: null,
            Loc.T("Fetch.PickerAutoNote"),
            isChecked: true));

        var separator = new Border();
        if (LookupStyle("PickerSeparator") is { } separatorStyle) separator.Style = separatorStyle;
        OptionsPanel.Children.Add(separator);

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
            var badgeText = new TextBlock { Text = badge };
            if (LookupStyle("PickerBadgeText") is { } badgeTextStyle) badgeText.Style = badgeTextStyle;

            var badgeBorder = new Border { Child = badgeText };
            if (LookupStyle("PickerBadge") is { } badgeStyle) badgeBorder.Style = badgeStyle;

            titleRow.Children.Add(badgeBorder);
        }

        var noteText = new TextBlock { Text = note };
        if (LookupStyle("PickerNoteText") is { } noteStyle) noteText.Style = noteStyle;

        var content = new StackPanel();
        content.Children.Add(titleRow);
        content.Children.Add(noteText);

        var radio = new RadioButton
        {
            GroupName = "source",
            Tag = id,
            IsChecked = isChecked,
            Content = content,
            Margin = new Thickness(0, 4, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        if (LookupStyle("PickerRadio") is { } radioStyle) radio.Style = radioStyle;

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
