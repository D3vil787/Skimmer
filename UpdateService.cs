using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Skimmer;

sealed class UpdateInfo
{
    public Version Version;
    public string Tag;
    public string Notes;
    public string PageUrl;
    public string AssetName;
    public string AssetUrl;
    public long AssetSize;
    public string SumsUrl;

    public string VersionText => UpdateService.Format(Version);
}

/// <summary>
/// Self-update from GitHub Releases (D3vil787/Skimmer).
///  1. GET /releases/latest, compare tag with the running version.
///  2. Download Skimmer.exe next to the running exe as *.new,
///     verify SHA-256 against the SHA256SUMS.txt asset of the same release.
///  3. Rename the running exe to *.old (allowed on Windows), move *.new into place,
///     restart after the main window is closed. Leftover *.old files are deleted at next start.
/// </summary>
static class UpdateService
{
    const string Repo = "D3vil787/Skimmer";
    const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";

    static readonly HttpClient http = CreateClient();

    /// <summary>Set by Install(); Program starts it after the application has exited.</summary>
    public static string PendingRestartPath;

    static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AllowAutoRedirect = true
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Skimmer/" + CurrentText);
        return c;
    }

    // ------------------------------------------------------------ versions

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Normalize(v);
        }
    }

    public static string CurrentText => Format(Current);

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    static Version Normalize(Version v) => new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    static bool TryParseTag(string tag, out Version v)
    {
        v = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;
        tag = tag.Trim().TrimStart('v', 'V');
        int cut = tag.IndexOfAny(new[] { '-', '+' });
        if (cut > 0) tag = tag.Substring(0, cut);
        if (!Version.TryParse(tag, out var parsed)) return false;
        v = Normalize(parsed);
        return true;
    }

    /// <summary>Releases contain one self-contained exe.</summary>
    const string AssetNameForThisBuild = "Skimmer.exe";

    /// <summary>False when the program folder is read-only or the exe is not a normal Skimmer.exe process.</summary>
    public static bool CanSelfUpdate
    {
        get
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
            return string.Equals(AppPaths.DataDir, AppPaths.ExeDir, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------ check

    /// <summary>Returns null when the running version is the latest. Throws on network / format errors.</summary>
    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        using var resp = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;   // no releases yet
        resp.EnsureSuccessStatusCode();

        string json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (!TryParseTag(tag, out var ver) || ver <= Current) return null;

        var info = new UpdateInfo
        {
            Version = ver,
            Tag = tag,
            Notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "",
            PageUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : $"https://github.com/{Repo}/releases",
            AssetName = AssetNameForThisBuild
        };

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.GetProperty("name").GetString();
                string url = a.GetProperty("browser_download_url").GetString();
                if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                if (string.Equals(name, info.AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    info.AssetUrl = url;
                    info.AssetSize = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                }
                else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    info.SumsUrl = url;
                }
            }
        }

        if (info.AssetUrl == null)
            throw new InvalidOperationException($"Release {tag} has no file {info.AssetName}");
        return info;
    }

    // ------------------------------------------------------------ download

    static string FindHash(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var parts = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            string name = parts[^1].TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Downloads and verifies the new exe. Returns the path of the verified *.new file.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int> progress, CancellationToken ct)
    {
        if (info.SumsUrl == null)
            throw new InvalidOperationException("Release has no SHA256SUMS.txt - refusing to install an unverified file");

        string sums = await http.GetStringAsync(info.SumsUrl, ct).ConfigureAwait(false);
        string expected = FindHash(sums, info.AssetName)
            ?? throw new InvalidOperationException($"SHA256SUMS.txt has no entry for {info.AssetName}");

        string target = Environment.ProcessPath + ".new";
        try
        {
            using (var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? info.AssetSize;
                using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buf = new byte[81920];
                long done = 0;
                int last = -1, n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    dst.Write(buf, 0, n);
                    sha.AppendData(buf, 0, n);
                    done += n;
                    if (total > 0)
                    {
                        int p = (int)Math.Min(100, done * 100 / total);
                        if (p != last) { last = p; progress?.Report(p); }
                    }
                }
                dst.Flush();

                string actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch: the downloaded file is damaged");
            }
        }
        catch
        {
            TryDelete(target);
            throw;
        }
        return target;
    }

    // ------------------------------------------------------------ install

    /// <summary>
    /// Swaps the files. The running exe is renamed (Windows allows that), the new one takes its name.
    /// The caller must then close the main window; Program starts the new exe afterwards.
    /// </summary>
    public static void Install(string newFile)
    {
        string exe = Environment.ProcessPath;
        string old = exe + ".old";
        TryDelete(old);
        if (File.Exists(old)) old = exe + ".old" + Environment.TickCount64;

        File.Move(exe, old);
        try
        {
            File.Move(newFile, exe);
        }
        catch
        {
            try { File.Move(old, exe); } catch { }
            throw;
        }
        PendingRestartPath = exe;
    }

    /// <summary>Called by Program after the main window has closed.</summary>
    public static void StartPendingRestart()
    {
        string exe = PendingRestartPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--updated")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });
        }
        catch { }
    }

    /// <summary>Deletes *.old / *.new left from a previous update (the old process may still be exiting, so retry).</summary>
    public static void CleanupLeftovers()
    {
        string exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        string dir = Path.GetDirectoryName(exe);
        string name = Path.GetFileName(exe);
        if (string.IsNullOrEmpty(dir)) return;

        Task.Run(() =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                bool left = false;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, name + ".old*").Concat(Directory.GetFiles(dir, name + ".new")))
                    {
                        if (!TryDelete(f)) left = true;
                    }
                }
                catch { return; }
                if (!left) return;
                Thread.Sleep(1000);
            }
        });
    }

    static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch { return false; }
    }
}
