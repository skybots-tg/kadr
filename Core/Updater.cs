using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kadr.Core
{
    /// <summary>
    /// Over-the-air updates from GitHub Releases: find the latest release, download its Kadr.exe,
    /// verify size and SHA-256, then swap the running exe (a running file can be renamed) and restart.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "skybots-tg/kadr";
        const string AssetName = "Kadr.exe";

        public sealed record Release(Version Version, string Tag, string DownloadUrl, long Size, string Sha256, string Notes, string PageUrl);

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd($"Kadr/{Current}");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        static string OldExe => Path.Combine(Installer.InstallDir, "Kadr.old.exe");
        static string DownloadDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr", "update");

        public static async Task<Release> FetchLatestAsync(CancellationToken ct = default)
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                string sha = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && d.GetString()!.StartsWith("sha256:")
                    ? d.GetString()!.Substring(7) : null;
                return new Release(new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)), tag,
                    a.GetProperty("browser_download_url").GetString(), a.GetProperty("size").GetInt64(), sha,
                    root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    root.TryGetProperty("html_url", out var h) ? h.GetString() : $"https://github.com/{Repo}/releases/latest");
            }
            return null;
        }

        public static bool IsNewer(Release r) => r != null && r.Version > Current;

        /// <summary>Downloads the release exe and returns its path once size and checksum match.</summary>
        public static async Task<string> DownloadAsync(Release r, IProgress<double> progress = null, CancellationToken ct = default)
        {
            var uri = new Uri(r.DownloadUrl);
            if (uri.Scheme != "https" || !uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.T("Неожиданный адрес загрузки", "Unexpected download address"));

            Directory.CreateDirectory(DownloadDir);
            string path = Path.Combine(DownloadDir, $"Kadr-{r.Version}.exe");
            if (File.Exists(path) && Verify(path, r)) return path;

            string part = path + ".part";
            using (var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(part);
                var buf = new byte[1 << 16];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    total += n;
                    progress?.Report(r.Size > 0 ? (double)total / r.Size : 0);
                }
            }
            File.Move(part, path, true);
            if (!Verify(path, r))
            {
                TryDelete(path);
                throw new InvalidDataException(L.T("Файл обновления повреждён — контрольная сумма не совпала", "The update file is corrupted — checksum mismatch"));
            }
            return path;
        }

        static bool Verify(string path, Release r)
        {
            var fi = new FileInfo(path);
            if (fi.Length != r.Size) return false;
            if (r.Sha256 == null) return true;
            using var fs = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(fs));
            return string.Equals(hash, r.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Swap the installed exe for the downloaded one. The running file is renamed, not deleted.</summary>
        public static void Apply(string newExe)
        {
            string exe = Installer.InstalledExe;
            TryDelete(OldExe);
            File.Move(exe, OldExe);
            try { File.Copy(newExe, exe, true); }
            catch
            {
                File.Move(OldExe, exe, true); // roll back
                throw;
            }
            TryDelete(newExe);
        }

        /// <summary>Remove leftovers of a previous update (the old exe can only go once it has exited).</summary>
        public static void Cleanup()
        {
            TryDelete(OldExe);
            try
            {
                if (Directory.Exists(DownloadDir))
                    foreach (var f in Directory.GetFiles(DownloadDir)) TryDelete(f);
            }
            catch { }
        }

        /// <summary>First lines of the release notes as plain text, for the "updated" card.</summary>
        /// Bilingual notes: the English part follows a heading that contains "English"; a release without it
        /// shows no lines to English users rather than Russian ones.
        public static string Summary(string notes, int maxLines = 3)
        {
            var all = (notes ?? "").Replace("\r", "").Split('\n');
            int en = Array.FindIndex(all, l => l.TrimStart().StartsWith("#") && l.Contains("English", StringComparison.OrdinalIgnoreCase));
            var part = L.En ? (en < 0 ? Array.Empty<string>() : all.Skip(en + 1)) : (en < 0 ? all : all.Take(en));
            var lines = part
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
                .Select(l => l.Substring(2).Replace("**", "").Replace("`", ""))
                .Take(maxLines);
            return string.Join("\n", lines);
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }
}
