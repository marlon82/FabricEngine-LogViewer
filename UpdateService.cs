using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace LogViewer2;

public sealed record AvailableUpdate(Version Version, string Tag, string Name, string DownloadUrl, long Size, bool IsPrerelease, string Notes);

public static class UpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/marlon82/FabricEngine-LogViewer/releases?per_page=100";

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public static async Task<AvailableUpdate?> FindUpdateAsync(bool includePrereleases)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(ReleasesUrl);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var candidates = new List<AvailableUpdate>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var prerelease = release.GetProperty("prerelease").GetBoolean();
            if (prerelease && !includePrereleases) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var version) || version <= CurrentVersion) continue;
            if (!release.TryGetProperty("assets", out var assets)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.Equals("LogViewer-2.0.exe", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("LogViewer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                candidates.Add(new AvailableUpdate(version, tag,
                    release.GetProperty("name").GetString() ?? tag, url,
                    asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    prerelease, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""));
                break;
            }
        }
        return candidates.OrderByDescending(x => x.Version).FirstOrDefault();
    }

    public static async Task<string> DownloadAsync(AvailableUpdate update, IProgress<int>? progress = null)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExtremeNetworks", "LogViewer2", "Updates", update.Tag);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "LogViewer-update.exe");
        var temporary = path + ".download";
        using var client = CreateClient();
        using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength ?? update.Size;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                if (length > 0) progress?.Report((int)(received * 100 / length));
            }
        }
        await using (var validation = File.OpenRead(temporary))
        {
            if (validation.Length < 1024 || validation.ReadByte() != 'M' || validation.ReadByte() != 'Z')
                throw new InvalidDataException("The downloaded update is not a valid Windows executable.");
        }
        File.Move(temporary, path, true);
        return path;
    }

    public static void StartInstaller(string downloadedPath)
    {
        var currentPath = Environment.ProcessPath ?? throw new IOException("Unable to determine the current application path.");
        Process.Start(new ProcessStartInfo(downloadedPath)
        {
            UseShellExecute = true,
            ArgumentList = { "--apply-update", currentPath, Environment.ProcessId.ToString() }
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FabricEngine-LogViewer/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var clean = tag.Trim().TrimStart('v', 'V');
        var separator = clean.IndexOfAny(['-', '+']);
        if (separator >= 0) clean = clean[..separator];
        return Version.TryParse(clean, out version!);
    }
}
