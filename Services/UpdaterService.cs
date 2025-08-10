using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Linq;
using System.IO;
using System;
using MDTadusMod;            // for IAppPaths
using Microsoft.Win32;       // registry

namespace MDTadusMod.Services
{
    public class UpdaterService
    {
        private readonly HttpClient _http;
        private readonly IAppPaths _paths;
        public UpdaterService(HttpClient http, IAppPaths paths) { _http = http; _paths = paths; }

        public record UpdateInfo(string DownloadUrl, string RemoteHash);

        private record Asset(string name, string browser_download_url);
        private record Release(string tag_name, string name, System.Collections.Generic.List<Asset> assets);

        string HashStorePath => _paths.Combine("Updates", "last_installer.sha256");

        public async Task<UpdateInfo?> CheckAsync()
        {
#if DEBUG
            Debug.WriteLine("Skipping update check in Debug build.");
            return null; // no update checks in Debug builds
#endif
            if (!OperatingSystem.IsWindows())
            {
                Debug.WriteLine("Skipping update check on non-Windows platform.");
                return null; // Windows-only
            }

            // Installed build? (portable won't have this key)
            var isInstalled = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Muledump.NET", "DataDir", null) is string;
            if (!isInstalled)
            {
                Debug.WriteLine("Skipping update check for portable build.");
                return null;
            }

            try
            {
                var url = "https://api.github.com/repos/TadusPro/Muledump.NET/releases/tags/latest";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("Muledump.NET-Updater");
                var res = await _http.SendAsync(req);
                if (!res.IsSuccessStatusCode) return null;

                var rel = JsonSerializer.Deserialize<Release>(
                    await res.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (rel is null) return null;

                var setup = rel.assets?.FirstOrDefault(a => a.name.EndsWith("-setup-windows-x64.exe", StringComparison.OrdinalIgnoreCase));
                if (setup is null) return null;

                // Prefer tiny .sha256 sidecar; fall back to hashing the remote EXE
                string? remoteHash = null;
                var sha = rel.assets.FirstOrDefault(a => a.name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));
                if (sha is not null)
                {
                    remoteHash = (await _http.GetStringAsync(sha.browser_download_url))
                                 .Trim()
                                 .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                }
                else
                {
                    using var s = await _http.GetStreamAsync(setup.browser_download_url);
                    remoteHash = await ComputeSHA256Async(s);
                }

                var local = LoadStoredHash();
                return string.Equals(local, remoteHash, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new UpdateInfo(setup.browser_download_url, remoteHash!);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> DownloadInstallerAsync(string url)
        {
            var dir = _paths.Combine("Updates");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Path.GetFileName(new Uri(url).LocalPath));
            using var s = await _http.GetStreamAsync(url);
            using var fs = File.Create(file);
            await s.CopyToAsync(fs);
            return file;
        }

        public void RunInstallerAndExit(string installerPath, string? newHash)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LOG",
                UseShellExecute = true,
                Verb = "runas"
            });
            if (!string.IsNullOrWhiteSpace(newHash)) SaveStoredHash(newHash!);
        }

        static async Task<string> ComputeSHA256Async(Stream stream)
        {
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream);
            return Convert.ToHexString(hash);
        }

        string? LoadStoredHash()
        {
            try
            {
                var p = HashStorePath;
                return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
            }
            catch { return null; }
        }

        void SaveStoredHash(string hash)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HashStorePath)!);
                File.WriteAllText(HashStorePath, hash.Trim());
            }
            catch { /* ignore */ }
        }
    }
}
