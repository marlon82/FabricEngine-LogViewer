using System.Windows;
using System.Windows.Controls;

namespace LogViewer2;

public sealed record GitHubCatalogAsset(string Name, string Url, long Size, string Release, string Family, bool Installed);

public sealed class CatalogSelectionWindow : Window
{
    private readonly Dictionary<string, CheckBox> _familyBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<GitHubCatalogAsset> _assets;
    public IReadOnlyList<GitHubCatalogAsset> SelectedAssets { get; private set; } = [];

    public CatalogSelectionWindow(Window owner, IReadOnlyList<GitHubCatalogAsset> assets, string detectedFamily, string targetFolder, bool german)
    {
        Owner = owner;
        _assets = assets;
        Title = german ? "Eventbibliotheken auswählen" : "Select event libraries";
        Width = 610;
        Height = 520;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var cancel = new Button { Content = german ? "Abbrechen" : "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var download = new Button { Content = german ? "Auswahl herunterladen" : "Download selection", MinWidth = 155, Style = (Style)Application.Current.Resources["PrimaryButton"] };
        download.Click += (_, _) => AcceptSelection(german);
        buttons.Children.Add(cancel);
        buttons.Children.Add(download);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = german ? "Gerätefamilien" : "Device families", FontSize = 18, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = german ? "Wähle die Familien aus, deren Eventbibliotheken installiert oder aktualisiert werden sollen." : "Select the families whose event libraries should be installed or updated.",
            Margin = new Thickness(0, 5, 0, 4), TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock { Text = (german ? "Ziel: " : "Destination: ") + targetFolder, Opacity = 0.72, TextWrapping = TextWrapping.Wrap });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var list = new StackPanel();
        scroll.Content = list;
        root.Children.Add(scroll);
        var families = assets.GroupBy(x => x.Family, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var family in families)
        {
            var installed = family.Count(x => x.Installed);
            var size = family.Sum(x => x.Size);
            var details = german
                ? $"{family.Count()} Katalog(e), {FormatBytes(size)} · {installed} bereits vorhanden"
                : $"{family.Count()} catalog(s), {FormatBytes(size)} · {installed} already installed";
            var box = new CheckBox
            {
                IsChecked = string.IsNullOrWhiteSpace(detectedFamily) || family.Key.Equals(detectedFamily, StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(3, 7, 3, 1),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = family.Key, FontSize = 14, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = details, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) }
                    }
                }
            };
            _familyBoxes[family.Key] = box;
            list.Children.Add(box);
        }
    }

    private void AcceptSelection(bool german)
    {
        var selected = _familyBoxes.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            MessageBox.Show(this, german ? "Wähle mindestens eine Gerätefamilie aus." : "Select at least one device family.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedAssets = _assets.Where(x => selected.Contains(x.Family)).ToArray();
        DialogResult = true;
        Close();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };
}

