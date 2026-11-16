using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace StratagemHelper;

/// <summary>
/// Checks GitHub for a newer release, downloads it, verifies it against the SHA-256 digest GitHub publishes for
/// the asset, and swaps it in. Only the self-contained release exe updates itself; source builds just get told.
/// The request carries no personal data, just a generic User-Agent.
/// </summary>
internal static class Updater
{
    public const string Repo = "gfyans/Helldivers-2-Stratagem-Helper";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
    public const string IssuesPage = "https://github.com/" + Repo + "/issues";

    /// <summary>A new GitHub issue with the app version filled in, so reports say which version they're about.</summary>
    public static string NewIssueUrl =>
        $"{IssuesPage}/new?body={Uri.EscapeDataString($"\n\n---\nStratagem Helper {Current}")}";
    private const string AssetName = "StratagemHelper.exe";

    // For testing against a local server: set STRATAGEMHELPER_UPDATE_URL to a JSON file shaped like GitHub's API.
    private static string ApiUrl =>
        Environment.GetEnvironmentVariable("STRATAGEMHELPER_UPDATE_URL") ?? $"https://api.github.com/repos/{Repo}/releases/latest";

    public sealed record Release(Version Version, string Tag, string PageUrl, string? DownloadUrl, string? Sha256);

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    private static string ExePath => Environment.ProcessPath!;
    private static string NewPath => ExePath + ".new";
    private static string OldPath => ExePath + ".old";

    /// <summary>
    /// True for the single-file release exe. A source build (exe + StratagemHelper.dll beside it) shouldn't be
    /// overwritten with a different kind of build.
    /// </summary>
    public static bool CanSelfUpdate =>
        !File.Exists(Path.Combine(Path.GetDirectoryName(ExePath)!, "StratagemHelper.dll"));

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StratagemHelper", Current.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The latest release if it's newer than this one, otherwise null.</summary>
    public static async Task<Release?> CheckAsync(CancellationToken ct = default)
    {
        using var http = NewClient();
        using var doc = JsonDocument.Parse(await http.GetStringAsync(ApiUrl, ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        if (version <= Current) return null;

        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        string? url = null, sha = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if (string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    url = a.GetProperty("browser_download_url").GetString();
                    if (a.TryGetProperty("digest", out var dg) && dg.GetString() is string s && s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = s[7..].ToLowerInvariant();
                }
        return new Release(version, tag, page, url, sha);
    }

    /// <summary>Downloads the release next to the exe as "*.new" and verifies its SHA-256. Returns the file path.</summary>
    public static async Task<string> DownloadAsync(Release r, CancellationToken ct = default)
    {
        if (r.DownloadUrl == null) throw new InvalidOperationException("The release has no StratagemHelper.exe to download.");
        if (r.Sha256 == null) throw new InvalidOperationException("The release has no checksum, so it can't be verified.");

        using var http = NewClient();
        var tmp = NewPath + ".part";
        await using (var src = await http.GetStreamAsync(r.DownloadUrl, ct))
        await using (var dst = File.Create(tmp))
            await src.CopyToAsync(dst, ct);

        string actual;
        await using (var f = File.OpenRead(tmp))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
        if (actual != r.Sha256)
        {
            File.Delete(tmp);
            throw new InvalidDataException("The download didn't match the release's checksum and was deleted.");
        }
        File.Move(tmp, NewPath, overwrite: true);
        return NewPath;
    }

    /// <summary>
    /// Swaps the downloaded exe in and starts it. Windows lets a running exe be renamed, so: current -> *.old,
    /// *.new -> current, launch. The caller must then exit; the new instance deletes *.old on startup.
    /// </summary>
    public static void InstallAndRestart(string newPath, bool startHidden)
    {
        if (File.Exists(OldPath)) File.Delete(OldPath);
        File.Move(ExePath, OldPath);
        try
        {
            File.Move(newPath, ExePath);
        }
        catch
        {
            File.Move(OldPath, ExePath); // put things back
            throw;
        }
        Process.Start(new ProcessStartInfo(ExePath, startHidden ? "--updated --tray" : "--updated") { UseShellExecute = false });
    }

    /// <summary>Removes leftovers from a previous update (called at startup).</summary>
    public static void CleanUp()
    {
        foreach (var path in new[] { OldPath, NewPath + ".part" })
            for (int i = 0; i < 10 && File.Exists(path); i++)
            {
                try { File.Delete(path); }
                catch { Thread.Sleep(300); } // the old process may still be exiting
            }
    }

    public static void OpenReleasesPage(string? url = null) =>
        Process.Start(new ProcessStartInfo(url ?? ReleasesPage) { UseShellExecute = true });
}
