using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Resolves a working yt-dlp path for TrailerDownloader regardless of which Emby Docker
    /// image/OS is in use: an explicit configured path or a PATH hit is used as-is, and only if
    /// neither works does this fall back to downloading the official standalone binary into the
    /// plugin's own persistent data folder - which, unlike anything installed into the container
    /// image itself, survives container recreation on any image (LinuxServer, official
    /// emby/embyserver, bare metal, ...). The official binaries bundle the EJS scripts yt-dlp
    /// needs for YouTube (together with a JS runtime, see DenoProvisioner). They have to keep up
    /// with YouTube's changes, so the managed copy is replaced once it's older than MaxManagedAge.
    /// </summary>
    public static class YtDlpProvisioner
    {
        private const string ReleaseBaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
        private static readonly TimeSpan MaxManagedAge = TimeSpan.FromDays(7);

        public static async Task<string> ResolveAsync(
            string configuredPath, string dataFolderPath, HttpClient httpClient, ILogger logger, CancellationToken cancellationToken)
        {
            if (IsUsableAbsolutePath(configuredPath) || IsOnPath(configuredPath))
            {
                return configuredPath;
            }

            var managedPath = Path.Combine(dataFolderPath, "bin", GetManagedFileName());
            var exists = File.Exists(managedPath);
            if (exists && DateTime.UtcNow - File.GetLastWriteTimeUtc(managedPath) < MaxManagedAge)
            {
                return managedPath;
            }

            // A failed update keeps the existing copy rather than leaving trailers without yt-dlp.
            var downloaded = await DownloadAsync(managedPath, httpClient, logger, cancellationToken).ConfigureAwait(false);
            return downloaded ?? (exists ? managedPath : configuredPath);
        }

        private static bool IsUsableAbsolutePath(string path) =>
            !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && File.Exists(path);

        internal static bool IsOnPath(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || Path.IsPathRooted(command))
            {
                return false;
            }

            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? command + ".exe" : command;
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            return pathVar.Split(Path.PathSeparator)
                .Where(dir => !string.IsNullOrWhiteSpace(dir))
                .Any(dir => File.Exists(Path.Combine(dir, fileName)));
        }

        private static string GetManagedFileName() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "yt-dlp.exe" : "yt-dlp";

        private static string GetReleaseAssetName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "yt-dlp.exe";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "yt-dlp_macos";
            }

            // "yt-dlp" (no suffix) is the platform-independent zipimport build, which needs a
            // system python3 - containers built for Emby generally don't have one. The
            // "yt-dlp_linux*" builds are self-contained PyInstaller binaries, so use those.
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "yt-dlp_linux_aarch64",
                Architecture.Arm => "yt-dlp_linux_armv7l",
                _ => "yt-dlp_linux"
            };
        }

        private static async Task<string?> DownloadAsync(
            string destinationPath, HttpClient httpClient, ILogger logger, CancellationToken cancellationToken)
        {
            var assetName = GetReleaseAssetName();
            var url = ReleaseBaseUrl + assetName;
            // Downloaded next to the target and renamed once complete, so an interrupted update
            // never replaces a working copy with a truncated one.
            var tempPath = destinationPath + ".download";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fileStream = File.Create(tempPath))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                MakeExecutable(tempPath);
                File.Move(tempPath, destinationPath, overwrite: true);

                logger.LogInformation("Coming Soon: downloaded yt-dlp ({Asset}) to {Path}", assetName, destinationPath);
                return destinationPath;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Coming Soon: failed to download yt-dlp from {Url}", url);
                TryDelete(tempPath);
                return null;
            }
        }

        internal static void MakeExecutable(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Overwritten by the next attempt.
            }
            catch (UnauthorizedAccessException)
            {
                // Overwritten by the next attempt.
            }
        }
    }
}
