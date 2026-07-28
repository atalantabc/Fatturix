using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FattureViewer.Services
{
    public sealed class UpdateInfo
    {
        public Version Version { get; init; } = new Version(0, 0, 0, 0);
        public string TagName { get; init; } = "";
        public string ReleaseNotes { get; init; } = "";
        public string InstallerName { get; init; } = "";
        public string InstallerUrl { get; init; } = "";
        public string? Sha256 { get; init; }
    }

    public static class UpdateService
    {
        private const string ReleasesUrl = "https://api.github.com/repos/atalantabc/Fatturix/releases?per_page=30";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static Version CurrentVersion =>
            NormalizeVersion(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

        public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            return SelectEligibleRelease(json, CurrentVersion);
        }

        public static UpdateInfo? SelectEligibleRelease(string releasesJson, Version currentVersion)
        {
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(releasesJson) ??
                           new List<GitHubRelease>();
            Version normalizedCurrent = NormalizeVersion(currentVersion);

            return releases
                .Where(release => !release.Draft &&
                                  !release.Prerelease &&
                                  !IsNoUpdateDescription(release.Body))
                .Select(release => CreateUpdateInfo(release))
                .Where(update => update != null && update.Version > normalizedCurrent)
                .Cast<UpdateInfo>()
                .OrderByDescending(update => update.Version)
                .FirstOrDefault();
        }

        public static bool IsNoUpdateDescription(string? description)
        {
            return !string.IsNullOrWhiteSpace(description) &&
                   description.TrimEnd().EndsWith("(N.U)", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetInstallerDownloadPath(UpdateInfo update)
        {
            string safeName = Path.GetFileName(update.InstallerName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = $"FattureViewerInstaller-{update.Version.ToString(3)}.exe";

            string directory = Path.Combine(
                Path.GetTempPath(),
                "FattureViewer",
                "Updates");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, safeName);
        }

        public static async Task DownloadInstallerAsync(
            UpdateInfo update,
            string destinationPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string partialPath = destinationPath + ".download";
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            DeleteIfExists(partialPath);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, update.InstallerUrl);
                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(
                    partialPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);

                byte[] buffer = new byte[81920];
                long downloadedBytes = 0;
                int read;
                progress?.Report(0);
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloadedBytes += read;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                        progress?.Report(downloadedBytes * 100d / totalBytes.Value);
                }

                await destination.FlushAsync(cancellationToken);
                destination.Close();

                if (!string.IsNullOrWhiteSpace(update.Sha256))
                {
                    using var sha256 = SHA256.Create();
                    await using var file = File.OpenRead(partialPath);
                    string actualHash = Convert.ToHexString(await sha256.ComputeHashAsync(file, cancellationToken));
                    if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Il controllo di integrità dell'installer non è riuscito.");
                }

                File.Move(partialPath, destinationPath, true);
                progress?.Report(100);
            }
            catch
            {
                DeleteIfExists(partialPath);
                throw;
            }
        }

        public static void StartInstaller(string installerPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"--update --silent --parent-pid {Environment.ProcessId} --restart",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!
            };

            if (Process.Start(startInfo) == null)
                throw new InvalidOperationException("Impossibile avviare l'installer dell'aggiornamento.");
        }

        private static UpdateInfo? CreateUpdateInfo(GitHubRelease release)
        {
            Version? version = ParseTagVersion(release.TagName);
            if (version == null)
                return null;

            GitHubAsset? installer = release.Assets
                .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(asset =>
                    asset.Name.StartsWith("FattureViewerInstaller-", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (installer == null || string.IsNullOrWhiteSpace(installer.DownloadUrl))
                return null;

            string? digest = installer.Digest;
            if (!string.IsNullOrWhiteSpace(digest) &&
                digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                digest = digest.Substring("sha256:".Length);
            }
            else
            {
                digest = null;
            }

            return new UpdateInfo
            {
                Version = version,
                TagName = release.TagName,
                ReleaseNotes = string.IsNullOrWhiteSpace(release.Body)
                    ? "È disponibile una nuova versione di FattureViewer."
                    : release.Body.Trim(),
                InstallerName = installer.Name,
                InstallerUrl = installer.DownloadUrl,
                Sha256 = digest
            };
        }

        private static Version? ParseTagVersion(string? tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                return null;

            string value = tagName.Trim().TrimStart('v', 'V');
            int suffixIndex = value.IndexOfAny(new[] { '-', '+' });
            if (suffixIndex >= 0)
                value = value.Substring(0, suffixIndex);

            return Version.TryParse(value, out Version? version)
                ? NormalizeVersion(version)
                : null;
        }

        private static Version NormalizeVersion(Version version)
        {
            return new Version(
                version.Major,
                Math.Max(version.Minor, 0),
                Math.Max(version.Build, 0),
                Math.Max(version.Revision, 0));
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FattureViewer-Updater/1.0");
            return client;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = "";

            [JsonPropertyName("body")]
            public string Body { get; set; } = "";

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; } = new List<GitHubAsset>();
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("browser_download_url")]
            public string DownloadUrl { get; set; } = "";

            [JsonPropertyName("digest")]
            public string? Digest { get; set; }
        }
    }
}
