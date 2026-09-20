using Microsoft.Win32;
using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace LogViewer2;

public partial class MainWindow : Window
{
    private readonly List<LogEntry> _entries = [];
    private readonly List<string> _files = [];
    private readonly HashSet<string> _moduleSelection = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _severitySelection = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _categorySelection = new(StringComparer.CurrentCultureIgnoreCase);
    private string _systemName = "", _systemType = "", _systemVersion = "", _macAddress = "";
    private string _catalogFolder;
    private bool _german = true;
    private bool _dark;
    private bool _includePrereleases;
    private ListSortDirection _dateSortDirection = ListSortDirection.Ascending;
    public ICollectionView View { get; }

    public MainWindow()
    {
        _catalogFolder = LoadCatalogFolder();
        _german = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase);
        InitializeComponent();
        _includePrereleases = LoadPrereleaseSetting();
        IncludePrereleasesMenu.IsChecked = _includePrereleases;
        View = CollectionViewSource.GetDefaultView(_entries);
        View.Filter = FilterEntry;
        DataContext = this;
        RefreshFacets();
        ApplyLanguage();
        if (IsSystemDarkMode()) ThemeButton_Click(this, new RoutedEventArgs());
        var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
        if (args.Length > 0) Loaded += async (_, _) => await LoadFilesAsync(args, false);
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return value is int number && number == 0;
        }
        catch { return false; }
    }

    private static string CatalogSettingsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExtremeNetworks", "LogViewer2");
    private static string LoadCatalogFolder()
    {
        var defaultFolder = Path.Combine(CatalogSettingsFolder, "EventCatalogs");
        try
        {
            var setting = Path.Combine(CatalogSettingsFolder, "catalog-folder.txt");
            if (File.Exists(setting) && Directory.Exists(File.ReadAllText(setting).Trim())) return File.ReadAllText(setting).Trim();
        }
        catch { }
        return defaultFolder;
    }

    private void SaveCatalogFolder()
    {
        Directory.CreateDirectory(CatalogSettingsFolder);
        File.WriteAllText(Path.Combine(CatalogSettingsFolder, "catalog-folder.txt"), _catalogFolder, Encoding.UTF8);
    }

    private static bool LoadPrereleaseSetting()
    {
        try { return File.Exists(Path.Combine(CatalogSettingsFolder, "include-prereleases.txt")) && bool.Parse(File.ReadAllText(Path.Combine(CatalogSettingsFolder, "include-prereleases.txt"))); }
        catch { return false; }
    }

    private void IncludePrereleases_Click(object sender, RoutedEventArgs e)
    {
        _includePrereleases = IncludePrereleasesMenu.IsChecked;
        Directory.CreateDirectory(CatalogSettingsFolder);
        File.WriteAllText(Path.Combine(CatalogSettingsFolder, "include-prereleases.txt"), _includePrereleases.ToString());
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        UpdateButton.IsEnabled = false;
        try
        {
            StatusText.Text = T("Suche nach Aktualisierungen …", "Checking for updates …");
            var update = await UpdateService.FindUpdateAsync(_includePrereleases);
            if (update is null)
            {
                StatusText.Text = T("LogViewer ist aktuell", "LogViewer is up to date");
                MessageBox.Show(this,
                    T($"Version {UpdateService.CurrentVersion.ToString(3)} ist aktuell.", $"Version {UpdateService.CurrentVersion.ToString(3)} is up to date."),
                    T("Keine Aktualisierung verfügbar", "No update available"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var channel = update.IsPrerelease ? "Pre-Release" : "Stable";
            var notes = string.IsNullOrWhiteSpace(update.Notes) ? "" : Environment.NewLine + Environment.NewLine + update.Notes.Trim();
            var question = T(
                $"Version {update.Tag} ({channel}, {FormatBytes(update.Size)}) ist verfügbar. Jetzt herunterladen und installieren?{notes}",
                $"Version {update.Tag} ({channel}, {FormatBytes(update.Size)}) is available. Download and install it now?{notes}");
            if (MessageBox.Show(this, question, T("LogViewer-Aktualisierung", "LogViewer update"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                StatusText.Text = T("Aktualisierung abgebrochen", "Update cancelled");
                return;
            }

            var progress = new Progress<int>(percent => StatusText.Text = T($"Aktualisierung wird geladen: {percent}%", $"Downloading update: {percent}%"));
            var downloadedPath = await UpdateService.DownloadAsync(update, progress);
            StatusText.Text = T("Aktualisierung wird installiert …", "Installing update …");
            UpdateService.StartInstaller(downloadedPath);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text = T("Aktualisierung fehlgeschlagen", "Update failed");
            MessageBox.Show(this, ex.Message, T("Aktualisierung fehlgeschlagen", "Update failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateButton.IsEnabled = true;
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e) => await ChooseAndLoad(false);
    private async void Add_Click(object sender, RoutedEventArgs e) => await ChooseAndLoad(true);
    private async Task ChooseAndLoad(bool append)
    {
        var d = new OpenFileDialog { Title = T("Logdateien öffnen", "Open log files"), Multiselect = true, Filter = "Log files|*.log;*.txt;*.0??;*.1??;*.2??;*.3??;*.4??;*.5??;*.6??;*.7??;*.8??;*.9??|All files|*.*" };
        if (d.ShowDialog() == true) await LoadFilesAsync(d.FileNames, append);
    }

    private async Task LoadFilesAsync(IEnumerable<string> paths, bool append)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(FileExtensionOrder).ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray(); if (files.Length == 0) return;
        SetBusy(true); StatusText.Text = T("Logdateien werden analysiert …", "Analyzing log files …");
        try
        {
            var result = await Task.Run(() => LogParser.ParseFiles(files));
            if (!append) { _entries.Clear(); _files.Clear(); }
            foreach (var item in result.Entries) { item.Id = _entries.Count + 1; _entries.Add(item); }
            foreach (var file in files) if (!_files.Contains(file, StringComparer.OrdinalIgnoreCase)) _files.Add(file);
            RefreshFacets(); View.Refresh(); ApplyDateSort(ListSortDirection.Ascending);
            if (!string.IsNullOrWhiteSpace(result.SystemName)) _systemName = result.SystemName;
            if (!string.IsNullOrWhiteSpace(result.SystemType)) _systemType = result.SystemType;
            if (!string.IsNullOrWhiteSpace(result.SystemVersion)) _systemVersion = result.SystemVersion;
            if (!string.IsNullOrWhiteSpace(result.MacAddress)) _macAddress = result.MacAddress;
            UpdateSystemInfo();
            SourceInfo.Text = string.Join(", ", _files.Select(Path.GetFileName));
            SourceInfo.ToolTip = string.Join(Environment.NewLine, _files);
            StatusText.Text = result.Skipped > 0 ? T($"Geladen · {result.Skipped:N0} nicht relevante Zeilen übersprungen", $"Loaded · {result.Skipped:N0} non-log lines skipped") : T("Geladen", "Loaded");
            UpdateCounts();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Fehler beim Laden", "Load error"), MessageBoxButton.OK, MessageBoxImage.Error); StatusText.Text = T("Fehler", "Error"); }
        finally { SetBusy(false); }
    }

    private static (int Group, long Number, string Text) FileExtensionOrder(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.');
        return long.TryParse(extension, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? (0, number, "")
            : (1, 0, extension);
    }

    private bool FilterEntry(object value)
    {
        if (value is not LogEntry x) return false;
        var q = SearchBox?.Text?.Trim() ?? "";
        bool text = q.Length == 0 || new[] { x.Date, x.Time, x.Module, x.EventId, x.AlarmId, x.Vrf, x.Category, x.Severity, x.Message, x.File }.Any(s => s.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        return text && SelectedFacet(_moduleSelection, x.Module) && SelectedFacet(_severitySelection, x.Severity) && SelectedFacet(_categorySelection, x.Category);
    }
    // The sets contain explicitly excluded values. Everything is enabled by default.
    private static bool SelectedFacet(HashSet<string> excluded, string value) => !excluded.Contains(value);
    private void Filter_Changed(object sender, EventArgs e) { View?.Refresh(); UpdateCounts(); }
    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear(); _moduleSelection.Clear(); _severitySelection.Clear(); _categorySelection.Clear(); RefreshFacets(); View.Refresh(); UpdateCounts();
    }
    private void RefreshFacets()
    {
        FillFilterMenu(ModuleFilterButton, ModuleFilterText, _moduleSelection, _entries.Select(x => x.Module));
        FillFilterMenu(SeverityFilterButton, SeverityFilterText, _severitySelection, _entries.Select(x => x.Severity));
        FillFilterMenu(CategoryFilterButton, CategoryFilterText, _categorySelection, _entries.Select(x => x.Category));
    }
    private void FillFilterMenu(Button button, TextBlock label, HashSet<string> selected, IEnumerable<string> values)
    {
        var menu = new ContextMenu { MaxHeight = 360 };
        var available = values.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s).ToArray();
        selected.RemoveWhere(value => !available.Contains(value, StringComparer.CurrentCultureIgnoreCase));
        foreach (var value in available)
        {
            var item = new MenuItem { Header = value, Tag = value, IsCheckable = true, IsChecked = !selected.Contains(value), StaysOpenOnClick = true };
            item.Click += (_, _) => { if (item.IsChecked) selected.Remove(value); else selected.Add(value); UpdateFilterLabel(label, selected, available.Length); View.Refresh(); UpdateCounts(); };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; UpdateFilterLabel(label, selected, available.Length);
    }
    private void FilterButton_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.ContextMenu is not null) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; } }
    private void UpdateFilterLabel(TextBlock label, HashSet<string> excluded, int total)
    {
        var active = Math.Max(0, total - excluded.Count);
        label.Text = excluded.Count == 0 ? T("Alle aktiviert  ▾", "All enabled  ▾") : T($"{active} von {total} aktiviert  ▾", $"{active} of {total} enabled  ▾");
    }
    private void UpdateCounts() { if (CountText is null) return; var shown = View.Cast<object>().Count(); CountText.Text = _german ? $"{shown:N0} von {_entries.Count:N0} Einträgen" : $"{shown:N0} of {_entries.Count:N0} entries"; FileText.Text = _files.Count == 0 ? T("Keine Datei geladen", "No file loaded") : (_files.Count == 1 ? Path.GetFileName(_files[0]) : T($"{_files.Count} Dateien", $"{_files.Count} files")); }
    private void SetBusy(bool busy) { Mouse.OverrideCursor = busy ? Cursors.Wait : null; OpenButton.IsEnabled = AddButton.IsEnabled = CatalogButton.IsEnabled = !busy; }

    private void Clear_Click(object sender, RoutedEventArgs e) { _entries.Clear(); _files.Clear(); _systemName = _systemType = _systemVersion = _macAddress = ""; RefreshFacets(); View.Refresh(); SystemInfo.Text = SourceInfo.Text = "—"; SystemInfo.ToolTip = SourceInfo.ToolTip = null; StatusText.Text = T("Bereit", "Ready"); UpdateCounts(); }
    private void UpdateSystemInfo()
    {
        var visible = new[] { _systemName, _systemVersion }.Where(x => !string.IsNullOrWhiteSpace(x));
        SystemInfo.Text = visible.Any() ? string.Join(" · ", visible) : "—";
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(_systemName)) details.Add(T("Systemname", "System name") + ": " + _systemName);
        if (!string.IsNullOrWhiteSpace(_systemVersion)) details.Add(T("Version", "Version") + ": " + _systemVersion);
        if (!string.IsNullOrWhiteSpace(_systemType)) details.Add(T("Systemtyp", "System type") + ": " + _systemType);
        if (!string.IsNullOrWhiteSpace(_macAddress)) details.Add(T("MAC-Adresse", "MAC address") + ": " + _macAddress);
        SystemInfo.ToolTip = details.Count == 0 ? null : string.Join(Environment.NewLine, details);
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0) return;
        var d = new SaveFileDialog { Title=T("Exportieren", "Export"), FileName="LogViewer-export.csv", Filter="CSV (*.csv)|*.csv|Text (*.txt)|*.txt" };
        if (d.ShowDialog() != true) return;
        var rows = View.Cast<LogEntry>().ToList();
        if (Path.GetExtension(d.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)) File.WriteAllLines(d.FileName, rows.Select(x => x.Message), new UTF8Encoding(true));
        else
        {
            string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            var lines = new List<string> { "Id;Date;Time;Module;EventId;AlarmId;VRF;Category;Severity;Message;File;Line" };
            lines.AddRange(rows.Select(x => string.Join(";", x.Id, Q(x.Date), Q(x.Time), Q(x.Module), Q(x.EventId), Q(x.AlarmId), Q(x.Vrf), Q(x.Category), Q(x.Severity), Q(x.Message), Q(x.File), x.LineNumber)));
            File.WriteAllLines(d.FileName, lines, new UTF8Encoding(true));
        }
        StatusText.Text = T("Export gespeichert", "Export saved");
    }
    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0) return;
        var rows = View.Cast<LogEntry>();
        string Section(string title, IEnumerable<IGrouping<string, LogEntry>> groups) => title + "\n" + string.Join("\n", groups.OrderByDescending(g => g.Count()).Take(20).Select(g => $"  {g.Key,-24} {g.Count(),8:N0}"));
        var text = Section(T("Schweregrad", "Severity"), rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Severity) ? "—" : x.Severity)) + "\n\n" + Section(T("Module", "Modules"), rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Module) ? "—" : x.Module)) + "\n\n" + Section(T("Kategorien", "Categories"), rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "—" : x.Category));
        ShowTextWindow(T("Statistik", "Statistics"), text, 620, 650);
    }
    private void Details_Click(object sender, RoutedEventArgs e) => ShowDetails();
    private void CatalogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }

    private async void ImportCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Event-Dokumentation konvertieren", "Convert event documentation"),
            Filter = "Event documentation (*.tar)|*.tar|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            Directory.CreateDirectory(_catalogFolder);
            var results = new List<string>();
            foreach (var file in dialog.FileNames)
            {
                StatusText.Text = T($"Konvertiere {Path.GetFileName(file)} …", $"Converting {Path.GetFileName(file)} …");
                var result = await EventCatalog.ConvertTarAsync(file, _catalogFolder);
                results.Add($"{Path.GetFileName(result.OutputPath)}: {result.Count:N0} Events");
            }
            StatusText.Text = T("Eventkatalog erstellt", "Event catalog created");
            MessageBox.Show(this, string.Join(Environment.NewLine, results) + Environment.NewLine + Environment.NewLine + _catalogFolder,
                T("Eventkatalog erstellt", "Event catalog created"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("Konvertierung fehlgeschlagen", "Conversion failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void ImportCatalogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("Ordner mit EDoc-TAR-Dateien auswählen", "Select folder containing EDoc TAR files") };
        if (dialog.ShowDialog(this) != true) return;
        var files = Directory.EnumerateFiles(dialog.FolderName, "*.tar", SearchOption.AllDirectories)
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (files.Length == 0)
        {
            MessageBox.Show(this, T("In diesem Ordner wurden keine TAR-Dateien gefunden.", "No TAR files were found in this folder."), T("Eventkatalog", "Event catalog"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetBusy(true);
        try
        {
            Directory.CreateDirectory(_catalogFolder);
            var converted = 0;
            long eventCount = 0;
            var failures = new List<string>();
            for (var index = 0; index < files.Length; index++)
            {
                var file = files[index];
                StatusText.Text = T($"Konvertiere {index + 1} von {files.Length}: {Path.GetFileName(file)} …", $"Converting {index + 1} of {files.Length}: {Path.GetFileName(file)} …");
                try
                {
                    var result = await EventCatalog.ConvertTarAsync(file, _catalogFolder);
                    converted++;
                    eventCount += result.Count;
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
            StatusText.Text = T("EDoc-Ordner verarbeitet", "EDoc folder processed");
            var summary = T($"{converted} von {files.Length} TAR-Dateien konvertiert\n{eventCount:N0} Events verarbeitet", $"Converted {converted} of {files.Length} TAR files\nProcessed {eventCount:N0} events");
            if (failures.Count > 0) summary += Environment.NewLine + Environment.NewLine + T("Fehler:", "Errors:") + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(12));
            summary += Environment.NewLine + Environment.NewLine + _catalogFolder;
            MessageBox.Show(this, summary, T("EDoc-Ordner verarbeitet", "EDoc folder processed"), MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private async void DownloadGitHubCatalogs_Click(object sender, RoutedEventArgs e)
    {
        const string releasesUrl = "https://api.github.com/repos/marlon82/FabricEngine-LogViewer/releases?per_page=100";
        SetBusy(true);
        try
        {
            StatusText.Text = T("Suche Eventkataloge auf GitHub …", "Searching GitHub for event catalogs …");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FabricEngine-LogViewer/2.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.GetAsync(releasesUrl);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(T(
                    "Das GitHub-Repository oder seine Releases sind derzeit nicht öffentlich erreichbar.",
                    "The GitHub repository or its releases are not currently publicly accessible."));
            response.EnsureSuccessStatusCode();

            await using var jsonStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(jsonStream);
            var assets = new Dictionary<string, GitHubCatalogAsset>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
                var tag = release.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
                if (!release.TryGetProperty("assets", out var releaseAssets)) continue;
                foreach (var asset in releaseAssets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (!name.EndsWith(".evdb", StringComparison.OrdinalIgnoreCase)) continue;
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    var size = asset.TryGetProperty("size", out var sizeValue) ? sizeValue.GetInt64() : 0;
                    var safeName = Path.GetFileName(name);
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                        assets.TryAdd(safeName, new GitHubCatalogAsset(safeName, url, size, tag, GetCatalogFamily(safeName), File.Exists(Path.Combine(_catalogFolder, safeName))));
                }
            }

            if (assets.Count == 0)
            {
                StatusText.Text = T("Keine GitHub-Kataloge gefunden", "No GitHub catalogs found");
                MessageBox.Show(this,
                    T("In den GitHub-Releases wurden keine .evdb-Dateien gefunden.", "No .evdb files were found in the GitHub releases."),
                    T("Eventkataloge von GitHub", "Event catalogs from GitHub"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var detectedFamily = Regex.Match(_systemType ?? "", @"(?<!\d)\d{4,5}(?!\d)").Value;
            var selection = new CatalogSelectionWindow(this, assets.Values.OrderBy(x => x.Family).ThenBy(x => x.Name).ToArray(), detectedFamily, _catalogFolder, _german);
            if (selection.ShowDialog() != true)
            {
                StatusText.Text = T("Download abgebrochen", "Download cancelled");
                return;
            }
            var selectedAssets = selection.SelectedAssets;

            Directory.CreateDirectory(_catalogFolder);
            var completed = 0;
            foreach (var asset in selectedAssets.OrderBy(x => x.Name))
            {
                completed++;
                StatusText.Text = T($"Lade {completed} von {selectedAssets.Count}: {asset.Name} …", $"Downloading {completed} of {selectedAssets.Count}: {asset.Name} …");
                var destination = Path.Combine(_catalogFolder, asset.Name);
                var temporary = destination + ".download";
                try
                {
                    using var download = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
                    download.EnsureSuccessStatusCode();
                    await using (var source = await download.Content.ReadAsStreamAsync())
                    await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                        await source.CopyToAsync(target);
                    _ = EventCatalog.Load(temporary);
                    File.Move(temporary, destination, true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }

            StatusText.Text = T($"{selectedAssets.Count} Eventkataloge installiert", $"Installed {selectedAssets.Count} event catalogs");
            MessageBox.Show(this,
                T($"{selectedAssets.Count} Eventkatalog(e) wurden installiert bzw. aktualisiert.\n\n{_catalogFolder}", $"{selectedAssets.Count} event catalog(s) were installed or updated.\n\n{_catalogFolder}"),
                T("Download abgeschlossen", "Download complete"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = T("GitHub-Download fehlgeschlagen", "GitHub download failed");
            MessageBox.Show(this, ex.Message, T("GitHub-Download fehlgeschlagen", "GitHub download failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };

    private static string GetCatalogFamily(string fileName)
    {
        var family = Regex.Match(Path.GetFileNameWithoutExtension(fileName), @"(?<!\d)\d{4,5}(?!\d)").Value;
        return family.Length > 0 ? family : "Other";
    }

    private void SelectCatalogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("Ordner mit Eventkatalogen auswählen", "Select event catalog folder"), InitialDirectory = _catalogFolder };
        if (dialog.ShowDialog(this) != true) return;
        _catalogFolder = dialog.FolderName;
        SaveCatalogFolder();
        StatusText.Text = T("Eventkatalog-Ordner ausgewählt", "Event catalog folder selected");
    }

    private void OpenCatalogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_catalogFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", _catalogFolder) { UseShellExecute = true });
    }

    private void ExplainEvent_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not LogEntry entry) return;
        var eventId = Regex.Match(entry.EventId ?? "", @"0x[0-9a-fA-F]+").Value;
        if (eventId.Length == 0)
        {
            MessageBox.Show(this, T("Diese Zeile enthält keine gültige Event-ID.", "This row does not contain a valid event ID."), T("Event-Erklärung", "Event explanation"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var catalogPath = EventCatalog.FindBestCatalog(_catalogFolder, _systemType, _systemVersion);
        if (catalogPath is null)
        {
            MessageBox.Show(this,
                T($"Kein passender Eventkatalog für {_systemType} / {_systemVersion} gefunden.\n\nImportiere zuerst die zugehörige TAR-Datei oder wähle den Ordner mit den .evdb-Dateien aus.\n\nKatalogordner: {_catalogFolder}",
                  $"No matching event catalog was found for {_systemType} / {_systemVersion}.\n\nImport the matching TAR file first or select the folder containing the .evdb files.\n\nCatalog folder: {_catalogFolder}"),
                T("Eventkatalog fehlt", "Event catalog missing"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var catalog = EventCatalog.Load(catalogPath);
            if (!catalog.Events.TryGetValue(eventId, out var info))
            {
                MessageBox.Show(this, T($"Event {eventId} ist im passenden Katalog nicht enthalten.", $"Event {eventId} is not contained in the matching catalog."), T("Event nicht gefunden", "Event not found"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var text = $"Event-ID: {info.EventId}\n{T("Katalog", "Catalog")}: {catalog.Product} {catalog.Version}\n{T("Schweregrad", "Severity")}: {info.Severity}\n{T("Alarmtyp", "Alarm type")}: {info.AlarmType}\n{T("Alarmgruppe", "Alarm group")}: {info.AlarmGroup}\n\n{T("Meldung", "Message")}\n{info.Message}\n\n{T("Details", "Details")}\n{info.Details}\n\n{T("Abhilfe", "Remedial action")}\n{info.Remedy}";
            ShowTextWindow(T($"Erklärung zu {eventId}", $"Explanation for {eventId}"), text, 820, 620);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("Eventkatalog kann nicht gelesen werden", "Unable to read event catalog"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LogGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ShowDetails();
    private void ShowDetails()
    {
        if (LogGrid.SelectedItem is not LogEntry x) return;
        var text = $"{T("Datum", "Date")}: {x.Date}\n{T("Zeit", "Time")}: {x.Time}\n{T("Modul", "Module")}: {x.Module}\nEvent-ID: {x.EventId}\nAlarm-ID: {x.AlarmId}\nVRF: {x.Vrf}\n{T("Kategorie", "Category")}: {x.Category}\n{T("Schweregrad", "Severity")}: {x.Severity}\n{T("Datei", "File")}: {x.File}\n{T("Zeile", "Line")}: {x.LineNumber}\n\n{x.Message}";
        ShowTextWindow(T("Eintragsdetails", "Entry details"), text, 760, 460);
    }
    private void ShowTextWindow(string title, string text, double width, double height)
    {
        var tb = new TextBox { Text=text, IsReadOnly=true, TextWrapping=TextWrapping.Wrap, AcceptsReturn=true, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, FontFamily=new System.Windows.Media.FontFamily("Consolas"), Margin=new Thickness(14) };
        new Window { Title=title, Owner=this, Width=width, Height=height, WindowStartupLocation=WindowStartupLocation.CenterOwner, Content=tb, Background=(System.Windows.Media.Brush)FindResource("Canvas") }.ShowDialog();
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not LogEntry x) return;
        var text = $"{x.Date}\t{x.Time}\t{x.Module}\t{x.EventId}\t{x.AlarmId}\t{x.Vrf}\t{x.Category}\t{x.Severity}\t{x.Message}";
        Exception? lastError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { Clipboard.SetDataObject(text, true); StatusText.Text = T("Zeile kopiert", "Row copied"); return; }
            catch (Exception ex) { lastError = ex; System.Threading.Thread.Sleep(40); }
        }
        MessageBox.Show(this, T("Die Zwischenablage wird gerade von einer anderen Anwendung verwendet.", "The clipboard is currently being used by another application.") + Environment.NewLine + lastError?.Message, T("Kopieren nicht möglich", "Unable to copy"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void FilterMessage_Click(object sender, RoutedEventArgs e) { if (LogGrid.SelectedItem is LogEntry x) SearchBox.Text = x.Message; }
    private void FilterEvent_Click(object sender, RoutedEventArgs e) { if (LogGrid.SelectedItem is LogEntry x && !string.IsNullOrWhiteSpace(x.EventId)) SearchBox.Text = x.EventId; }
    private void LogGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not DataGridRow) element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        if (element is DataGridRow row) { row.IsSelected = true; LogGrid.SelectedItem = row.Item; }
    }
    private void Window_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private async void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) await LoadFilesAsync(f, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); }
    private void LogGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column != DateColumn) { if (View is ListCollectionView otherView) otherView.CustomSort = null; StatusText.Text = T("Sortiert", "Sorted"); return; }
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplyDateSort(direction);
    }

    private void SortButton_Click(object sender, RoutedEventArgs e) => ApplyDateSort(_dateSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending);
    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void ColumnVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        var column = item.Tag?.ToString() switch
        {
            "Id" => IdColumn,
            "Date" => DateColumn,
            "Time" => TimeColumn,
            "Module" => ModuleColumn,
            "Event" => EventColumn,
            "Alarm" => AlarmColumn,
            "Vrf" => VrfColumn,
            "Category" => CategoryColumn,
            "Severity" => SeverityColumn,
            "Message" => MessageColumn,
            _ => null
        };
        if (column is not null) column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyDateSort(ListSortDirection direction)
    {
        _dateSortDirection = direction;
        foreach (var column in LogGrid.Columns) column.SortDirection = null;
        DateColumn.SortDirection = direction;
        if (View is ListCollectionView listView) listView.CustomSort = new DateTimeEntryComparer(direction);
        UpdateSortButton();
        StatusText.Text = direction == ListSortDirection.Ascending ? T("Älteste Einträge zuerst", "Oldest entries first") : T("Neueste Einträge zuerst", "Newest entries first");
    }
    private void UpdateSortButton()
    {
        var ascending = _dateSortDirection == ListSortDirection.Ascending;
        SortIcon.Text = ascending ? "\uE74A" : "\uE74B";
        SortLabel.Text = ascending ? T("Älteste zuerst", "Oldest first") : T("Neueste zuerst", "Newest first");
        SortButton.ToolTip = ascending ? T("Sortierung umkehren: neueste zuerst", "Reverse sorting: newest first") : T("Sortierung umkehren: älteste zuerst", "Reverse sorting: oldest first");
    }

    private sealed class DateTimeEntryComparer(ListSortDirection direction) : IComparer
    {
        public int Compare(object? left, object? right)
        {
            if (left is not LogEntry a || right is not LogEntry b) return 0;
            var result = ParseTimestamp(a).CompareTo(ParseTimestamp(b));
            if (result == 0) result = a.Id.CompareTo(b.Id);
            return direction == ListSortDirection.Ascending ? result : -result;
        }
        private static DateTimeOffset ParseTimestamp(LogEntry entry)
        {
            var value = string.IsNullOrWhiteSpace(entry.Date) ? entry.Time : $"{entry.Date}T{entry.Time}";
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var timestamp)) return timestamp;
            if (DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out timestamp)) return timestamp;
            return DateTimeOffset.MinValue;
        }
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e) { _german = !_german; ApplyLanguage(); }
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        SetBrush("Header", _dark ? "#1A1D24" : "#FFFFFF"); SetBrush("Canvas", _dark ? "#111318" : "#F4F6FA"); SetBrush("Panel", _dark ? "#1A1D24" : "#FFFFFF");
        SetBrush("Subtle", _dark ? "#222630" : "#F8FAFD"); SetBrush("Alternate", _dark ? "#282231" : "#F2EEFA"); SetBrush("Ink", _dark ? "#F2F4F8" : "#172033");
        SetBrush("Muted", _dark ? "#AEB5C2" : "#697386"); SetBrush("Line", _dark ? "#303541" : "#E4E8F0"); ThemeLabel.Text = _dark ? T("Hell", "Light") : T("Dunkel", "Dark"); ThemeButton.ToolTip = T(_dark ? "Heller Modus" : "Dunkler Modus", _dark ? "Light mode" : "Dark mode");
        SetBrush("Accent", _dark ? "#A78BFA" : "#7C3AED"); SetBrush("AccentDark", _dark ? "#C4B5FD" : "#6D28D9");
        SetBrush("Selection", _dark ? "#4C356F" : "#EDE9FE"); SetBrush("SelectionText", _dark ? "#FFFFFF" : "#5B21B6");
    }
    private static void SetBrush(string key, string color) => Application.Current.Resources[key] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private string T(string de, string en) => _german ? de : en;
    private void ApplyLanguage()
    {
        Title="Extreme Networks FabricEngine (VOSS) Log Viewer"; SubtitleText.Text=T("Netzwerk-Loganalyse · Version 2.0.0 Build 11", "Network log analysis · Version 2.0.0 Build 11"); UpdateLabel.Text="Update"; UpdateButton.ToolTip=T("LogViewer aktualisieren", "Update LogViewer"); CheckUpdatesMenu.Header=T("Jetzt nach Updates suchen …", "Check for updates now …"); IncludePrereleasesMenu.Header=T("Pre-Releases einbeziehen", "Include pre-releases"); LanguageLabel.Text=_german ? "DE" : "EN"; LanguageButton.ToolTip=T("Sprache zu Englisch wechseln", "Switch language to German"); ThemeLabel.Text = _dark ? T("Hell", "Light") : T("Dunkel", "Dark");
        OpenLabel.Text=T("Öffnen", "Open"); AddLabel.Text=T("Weitere Datei", "Add files"); ColumnsLabel.Text=T("Spalten", "Columns"); CatalogLabel.Text=T("Eventkatalog", "Event catalog"); ExportLabel.Text=T("Exportieren", "Export"); ClearLabel.Text=T("Entladen", "Unload"); StatsLabel.Text=T("Statistik", "Statistics");
        UpdateSortButton();
        ResetButton.Content=T("Filter zurücksetzen", "Reset filters"); SearchBox.ToolTip=T("Alle Felder durchsuchen", "Search all fields"); SystemLabel.Text=T("System:", "System:"); SourceLabel.Text=T("Quelle:", "Source:"); DropHint.Text=T("Dateien hier ablegen", "Drop files here"); FilterHint.Text=T("Suche · Modul · Schweregrad · Kategorie", "Search · Module · Severity · Category"); FooterHint.Text=T("Doppelklick öffnet Details · Spaltenköpfe sortieren", "Double-click for details · Click headers to sort");
        SearchFieldLabel.Text=T("Suche in allen Feldern", "Search all fields"); ModuleFieldLabel.Text=T("Module auswählen", "Select modules"); SeverityFieldLabel.Text=T("Schweregrade auswählen", "Select severities"); CategoryFieldLabel.Text=T("Kategorien auswählen", "Select categories");
        DateColumn.Header=T("Datum", "Date"); TimeColumn.Header=T("Zeit", "Time"); ModuleColumn.Header=T("Modul", "Module"); CategoryColumn.Header=T("Kategorie", "Category"); SeverityColumn.Header=T("Schweregrad", "Severity"); MessageColumn.Header=T("Meldung", "Message"); ColumnsButton.ToolTip=T("Spalten ein- oder ausblenden", "Show or hide columns"); ShowIdMenu.Header=T("Nummer", "Number"); ShowDateMenu.Header=T("Datum", "Date"); ShowTimeMenu.Header=T("Zeit", "Time"); ShowModuleMenu.Header=T("Modul", "Module"); ShowEventMenu.Header="Event-ID"; ShowAlarmMenu.Header="Alarm-ID"; ShowVrfMenu.Header="VRF"; ShowCategoryMenu.Header=T("Kategorie", "Category"); ShowSeverityMenu.Header=T("Schweregrad", "Severity"); ShowMessageMenu.Header=T("Meldung", "Message"); CopyMenu.Header=T("Zeile kopieren", "Copy row"); FilterMessageMenu.Header=T("Meldung als Filter verwenden", "Use message as filter"); FilterEventMenu.Header=T("Event-ID als Filter verwenden", "Use event ID as filter"); ExplainEventMenu.Header=T("Event erklären", "Explain event"); DetailsMenu.Header=T("Details", "Details");
        CatalogButton.ToolTip=T("Event-Dokumentation verwalten", "Manage event documentation"); ImportCatalogMenu.Header=T("TAR-Datei konvertieren …", "Convert TAR file …"); ImportCatalogFolderMenu.Header=T("Ordner mit TAR-Dateien konvertieren …", "Convert folder containing TAR files …"); DownloadGitHubCatalogsMenu.Header=T("Kataloge von GitHub aktualisieren …", "Update catalogs from GitHub …"); SelectCatalogFolderMenu.Header=T("Katalogordner auswählen …", "Select catalog folder …"); OpenCatalogFolderMenu.Header=T("Katalogordner öffnen", "Open catalog folder");
        RefreshFacets(); UpdateCounts(); if (_entries.Count == 0) StatusText.Text=T("Bereit", "Ready");
        UpdateSystemInfo();
        ThemeButton.ToolTip = T(_dark ? "Heller Modus" : "Dunkler Modus", _dark ? "Light mode" : "Dark mode");
    }
}
