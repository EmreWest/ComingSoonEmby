using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// The JavaScript runtime handed to yt-dlp via --js-runtimes. Argument is the option value
    /// ("deno:/path/to/deno"); CacheDirectory, when set, becomes DENO_DIR for the yt-dlp process.
    /// </summary>
    public sealed record JsRuntime(string Argument, string? CacheDirectory);

    /// <summary>
    /// yt-dlp needs an external JavaScript runtime to solve YouTube's player challenges - without
    /// one most formats are missing and downloads fail with "Requested format is not available".
    /// Deno is yt-dlp's default runtime, so it's managed like yt-dlp itself (see
    /// YtDlpProvisioner): an explicitly configured runtime is used as-is, then a working deno on
    /// PATH, and otherwise the official build is downloaded into the plugin's tools folder.
    /// The EJS scripts don't need to be fetched (e.g. via --remote-components): the official
    /// yt-dlp binaries already bundle them.
    /// </summary>
    public static class DenoProvisioner
    {
        private const string ReleaseBaseUrl = "https://github.com/denoland/deno/releases/latest/download/";
        private static readonly Version MinimumVersion = new(2, 3, 0);
        private static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds(30);
        private static readonly string[] OtherRuntimes = { "node", "bun", "quickjs" };

        /// <summary>Returns the runtime to pass to yt-dlp, or null if none is usable (logged).</summary>
        public static async Task<JsRuntime?> ResolveAsync(
            string? configuredRuntime, string dataFolderPath, HttpClient httpClient, ILogger logger, CancellationToken cancellationToken)
        {
            var toolsPath = Path.Combine(dataFolderPath, "bin");
            var cacheDirectory = Path.Combine(toolsPath, "deno-cache");
            var configured = (configuredRuntime ?? string.Empty).Trim();

            if (configured.Length > 0)
            {
                // "node:/usr/bin/node" etc. is passed through for runtimes we don't manage.
                foreach (var runtime in OtherRuntimes)
                {
                    if (configured.Equals(runtime, StringComparison.OrdinalIgnoreCase)
                        || configured.StartsWith(runtime + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        return new JsRuntime(configured, null);
                    }
                }

                var configuredPath = configured.StartsWith("deno:", StringComparison.OrdinalIgnoreCase)
                    ? configured.Substring("deno:".Length)
                    : configured;
                return await CheckAsync(configuredPath, logger, cancellationToken).ConfigureAwait(false) is { } configuredVersion
                    && IsSupported(configuredVersion, configuredPath, logger)
                    ? new JsRuntime("deno:" + configuredPath, cacheDirectory)
                    : null;
            }

            var managedPath = Path.Combine(toolsPath, GetManagedFileName());
            if (File.Exists(managedPath))
            {
                var managedVersion = await CheckAsync(managedPath, logger, cancellationToken).ConfigureAwait(false);
                if (managedVersion is null)
                {
                    // Re-downloading wouldn't help if the binary can't run on this system (e.g. a
                    // too old glibc), so don't fetch 40 MB again on every refresh.
                    return null;
                }

                if (managedVersion >= MinimumVersion)
                {
                    return new JsRuntime("deno:" + managedPath, cacheDirectory);
                }

                logger.LogInformation(
                    "Coming Soon: managed deno {Version} is older than {Minimum}, updating", managedVersion, MinimumVersion);
            }
            else if (YtDlpProvisioner.IsOnPath("deno"))
            {
                var pathVersion = await CheckAsync("deno", logger, cancellationToken).ConfigureAwait(false);
                if (pathVersion is not null && pathVersion >= MinimumVersion)
                {
                    return new JsRuntime("deno", cacheDirectory);
                }
            }

            var assetName = GetReleaseAssetName();
            if (assetName is null)
            {
                logger.LogWarning(
                    "Coming Soon: no Deno build exists for {Os}/{Arch}, so YouTube trailers will likely fail. Install node (22+) or quickjs and enter it as JS runtime, e.g. \"node:/usr/bin/node\".",
                    RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
                return null;
            }

            if (!await DownloadAsync(assetName, managedPath, httpClient, logger, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return await CheckAsync(managedPath, logger, cancellationToken).ConfigureAwait(false) is { } version
                && IsSupported(version, managedPath, logger)
                ? new JsRuntime("deno:" + managedPath, cacheDirectory)
                : null;
        }

        private static bool IsSupported(Version version, string path, ILogger logger)
        {
            if (version >= MinimumVersion)
            {
                return true;
            }

            logger.LogWarning(
                "Coming Soon: deno {Version} at {Path} is too old for yt-dlp (needs {Minimum}+) - YouTube trailers will likely fail.",
                version, path, MinimumVersion);
            return false;
        }

        /// <summary>
        /// Runs "deno --version" the way yt-dlp will start it. Returns the version, or null (with a
        /// warning naming the cause) if deno doesn't start on this system.
        /// </summary>
        private static async Task<Version?> CheckAsync(string denoPath, ILogger logger, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = denoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");
            startInfo.Environment["DENO_NO_UPDATE_CHECK"] = "1";
            TrailerDownloader.PreferSystemDynamicLinker(startInfo);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                var stdOut = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stdErr = process.StandardError.ReadToEndAsync(cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(VersionCheckTimeout);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                var output = await stdOut.ConfigureAwait(false);
                var match = Regex.Match(output, @"deno (\d+)\.(\d+)\.(\d+)");
                if (process.ExitCode == 0 && match.Success)
                {
                    return new Version(
                        int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
                }

                logger.LogWarning(
                    "Coming Soon: deno at {Path} does not start (exit code {Code}): {Error} - YouTube trailers will likely fail. If a library is missing (e.g. GLIBC), use an Emby image with a newer glibc or enter another JS runtime such as \"node:/usr/bin/node\".",
                    denoPath, process.ExitCode, (await stdErr.ConfigureAwait(false)).Trim());
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                logger.LogWarning("Coming Soon: deno at {Path} did not answer within {Timeout}", denoPath, VersionCheckTimeout);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Coming Soon: could not start deno at {Path} - YouTube trailers will likely fail", denoPath);
                return null;
            }
        }

        private static async Task<bool> DownloadAsync(
            string assetName, string destinationPath, HttpClient httpClient, ILogger logger, CancellationToken cancellationToken)
        {
            var url = ReleaseBaseUrl + assetName;
            var zipPath = destinationPath + ".zip.download";
            var tempPath = destinationPath + ".download";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var fileStream = File.Create(zipPath);
                    await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.GetEntry(GetManagedFileName())
                        ?? throw new InvalidDataException($"{assetName} does not contain {GetManagedFileName()}");
                    entry.ExtractToFile(tempPath, overwrite: true);
                }

                YtDlpProvisioner.MakeExecutable(tempPath);
                File.Move(tempPath, destinationPath, overwrite: true);

                logger.LogInformation("Coming Soon: downloaded deno ({Asset}) to {Path}", assetName, destinationPath);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Coming Soon: failed to download deno from {Url} - YouTube trailers will likely fail", url);
                return false;
            }
            finally
            {
                YtDlpProvisioner.TryDelete(zipPath);
                YtDlpProvisioner.TryDelete(tempPath);
            }
        }

        private static string GetManagedFileName() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "deno.exe" : "deno";

        // The "deno" archives, not "denort": denort is only the runtime for compiled deno apps
        // and can't run the scripts yt-dlp passes it.
        private static string? GetReleaseAssetName()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                _ => null
            };

            if (arch is null)
            {
                return null;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return arch == "x86_64" ? "deno-x86_64-pc-windows-msvc.zip" : null;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? $"deno-{arch}-apple-darwin.zip"
                : $"deno-{arch}-unknown-linux-gnu.zip";
        }
    }
}
