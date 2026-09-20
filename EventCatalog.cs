using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogViewer2;

public sealed record EventDescription(
    string EventId,
    string Message,
    string AlarmGroup,
    string Details,
    string Remedy,
    string AlarmType,
    string Severity);

public sealed record EventCatalogData(
    int FormatVersion,
    string Product,
    string Version,
    DateTime CreatedUtc,
    Dictionary<string, EventDescription> Events);

public static class EventCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static async Task<(string OutputPath, int Count)> ConvertTarAsync(string tarPath, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var events = new Dictionary<string, EventDescription>(StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(tarPath);
        using var reader = new TarReader(stream);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null ||
                !Path.GetFileName(entry.Name).StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !entry.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;

            using var textReader = new StreamReader(entry.DataStream, Encoding.UTF8, true, 4096, leaveOpen: false);
            var html = await textReader.ReadToEndAsync();
            var description = ParseHtml(Path.GetFileNameWithoutExtension(entry.Name), html);
            events[description.EventId] = description;
        }

        var (product, version) = ParseCatalogName(Path.GetFileNameWithoutExtension(tarPath));
        var data = new EventCatalogData(1, product, version, DateTime.UtcNow, events);
        var safeName = string.Join("_", new[] { product, version }.Where(x => x.Length > 0)).Replace(' ', '_');
        if (safeName.Length == 0) safeName = Path.GetFileNameWithoutExtension(tarPath).Replace("_edoc", "", StringComparison.OrdinalIgnoreCase);
        var outputPath = Path.Combine(outputFolder, safeName + ".evdb");
        await using var output = File.Create(outputPath);
        await using var gzip = new GZipStream(output, CompressionLevel.SmallestSize);
        await JsonSerializer.SerializeAsync(gzip, data, JsonOptions);
        return (outputPath, events.Count);
    }

    public static EventCatalogData Load(string path)
    {
        using var input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<EventCatalogData>(gzip, JsonOptions)
            ?? throw new InvalidDataException("Invalid event catalog.");
    }

    public static string? FindBestCatalog(string folder, string systemType, string version)
    {
        if (!Directory.Exists(folder)) return null;
        var family = Regex.Match(systemType ?? "", @"\d{4,5}").Value;
        var normalizedVersion = NormalizeVersion(version);
        var candidates = new List<(string Path, int Score)>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.evdb", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var data = Load(path);
                var score = 0;
                if (family.Length > 0 && data.Product.Contains(family, StringComparison.OrdinalIgnoreCase)) score += 100;
                var catalogVersion = NormalizeVersion(data.Version);
                if (catalogVersion.Length > 0 && catalogVersion.Equals(normalizedVersion, StringComparison.OrdinalIgnoreCase)) score += 50;
                else if (catalogVersion.Length > 0 && normalizedVersion.StartsWith(catalogVersion, StringComparison.OrdinalIgnoreCase)) score += 20;
                if (score > 0) candidates.Add((path, score));
            }
            catch { }
        }
        return candidates.OrderByDescending(x => x.Score).ThenByDescending(x => x.Path).FirstOrDefault().Path;
    }

    private static (string Product, string Version) ParseCatalogName(string name)
    {
        name = Regex.Replace(name, "_edoc$", "", RegexOptions.IgnoreCase);
        var match = Regex.Match(name, @"^(?<product>.+?)\.(?<version>\d+(?:\.\d+)+)$");
        return match.Success ? (match.Groups["product"].Value, match.Groups["version"].Value) : (name, "");
    }

    private static string NormalizeVersion(string value) => Regex.Match(value ?? "", @"\d+(?:\.\d+)+").Value;

    private static EventDescription ParseHtml(string fallbackId, string html)
    {
        string Section(string heading)
        {
            var match = Regex.Match(html, $@"<h2[^>]*>\s*{Regex.Escape(heading)}\s*</h2>\s*<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? Clean(match.Groups[1].Value) : "";
        }
        string Attribute(string name)
        {
            var match = Regex.Match(html, $@"<td[^>]*>\s*{Regex.Escape(name)}\s*</td>\s*<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? Clean(match.Groups[1].Value) : "";
        }
        var id = Section("Alarm ID");
        var title = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return new EventDescription(
            id.Length > 0 ? id : fallbackId,
            title.Success ? Clean(title.Groups[1].Value) : "",
            Section("Alarm group"),
            Section("Details"),
            Section("Remedial action"),
            Attribute("AlarmType"),
            Attribute("Severity"));
    }

    private static string Clean(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ")).Trim();
}
