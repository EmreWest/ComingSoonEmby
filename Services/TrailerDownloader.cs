using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Shells out to yt-dlp to pull a trailer down so it plays through Emby's own player instead
    /// of opening YouTube - which kicks tvOS/Apple TV users into a separate app. Depending on the
    /// caller the file becomes the entry's main video or a local trailer extra ("trailers"
    /// subfolder, Emby's native "Trailer" button).
    ///
    /// Every download and copy is staged in a private working directory outside the libraries
    /// and only moved into place once complete. The libraries use Emby's realtime monitor, which
    /// would otherwise pick up half-written files and yt-dlp's .part/fragment files.
    /// </summary>
    public class TrailerDownloader
    {
        public const string TrailerFileName = "trailer.mp4";

        private readonly ILogger _logger;
        private readonly string _ytDlpPath;
        private readonly string _ffmpegPath;
        private readonly string _workingDirectory;
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

        public TrailerDownloader(ILogger logger, string ytDlpPath, string ffmpegPath, string workingDirectory)
        {
            _logger = logger;
            _ytDlpPath = string.IsNullOrWhiteSpace(ytDlpPath) ? "yt-dlp" : ytDlpPath;
            _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;
            _workingDirectory = workingDirectory;
        }

        /// <summary>Removes staging folders left behind by a refresh that was interrupted.</summary>
        public void ClearWorkingDirectory() => TryDeleteDirectory(_workingDirectory);

        /// <summary>
        /// Copies an already-downloaded trailer to outputPath instead of fetching it again, used
        /// when several episodes of the same series (or the same movie in several lists) share one
        /// trailer. Returns outputPath, or null if the copy failed.
        /// </summary>
        public string? ReuseExisting(string existingTrailerPath, string outputPath)
        {
            var stagingDirectory = CreateStagingDirectory();
            try
            {
                var stagedPath = Path.Combine(stagingDirectory, TrailerFileName);
                File.Copy(existingTrailerPath, stagedPath);
                return Publish(stagedPath, outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not reuse trailer from {Source} for {Target}", existingTrailerPath, outputPath);
                return null;
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }

        /// <summary>
        /// Downloads the given YouTube video to outputPath, replacing whatever file is there.
        /// Returns the path on success, or null if the download failed or yt-dlp isn't available -
        /// the caller keeps the entry without a trailer, and the next refresh retries.
        /// </summary>
        public async Task<string?> DownloadAsync(
            string youtubeKey,
            string outputPath,
            CancellationToken cancellationToken)
        {
            var youtubeUrl = $"https://www.youtube.com/watch?v={youtubeKey}";
            var stagingDirectory = CreateStagingDirectory();
            var stagedPath = Path.Combine(stagingDirectory, TrailerFileName);

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            PreferSystemDynamicLinker(startInfo);

            var haveFfmpeg = HasUsableFfmpeg(_ffmpegPath);

            // ArgumentList passes each argument atomically (no shell involved), avoiding any
            // injection risk from youtubeKey containing quotes/special characters.
            startInfo.ArgumentList.Add("--no-progress");
            startInfo.ArgumentList.Add("-f");
            // Prefer a progressive (already-muxed, single-file) stream so no ffmpeg merge is
            // needed - YouTube serves progressive mp4 up to 720p, plenty for a trailer, and it
            // works even when ffmpeg is missing or too old. Only fall back to separate streams
            // (which need ffmpeg to merge) when a usable ffmpeg is available.
            startInfo.ArgumentList.Add(haveFfmpeg
                ? "best[ext=mp4][vcodec!=none][acodec!=none]/bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
                : "best[ext=mp4][vcodec!=none][acodec!=none]/best[vcodec!=none][acodec!=none]/best");

            if (haveFfmpeg)
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(_ffmpegPath);
                startInfo.ArgumentList.Add("--merge-output-format");
                startInfo.ArgumentList.Add("mp4");
            }

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(stagedPath);
            startInfo.ArgumentList.Add(youtubeUrl);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                var stdErr = new StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

                process.Start();
                process.BeginErrorReadLine();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(ProcessTimeout);

                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning(
                        "yt-dlp exited with code {Code} for {Url}: {Error}",
                        process.ExitCode, youtubeUrl, stdErr.ToString());
                    return null;
                }

                if (!File.Exists(stagedPath))
                {
                    _logger.LogWarning("yt-dlp reported success but output file is missing: {Path}", stagedPath);
                    return null;
                }

                return Publish(stagedPath, outputPath);
            }
            catch (OperationCanceledException)
            {
                // WaitForExitAsync only stops waiting - the process itself keeps running.
                TryKill(process);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _logger.LogWarning("yt-dlp download timed out after {Timeout} for {Url}", ProcessTimeout, youtubeUrl);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run yt-dlp (path: {Path}) - is it installed and reachable?", _ytDlpPath);
                return null;
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }

        // The working directory sits next to the library folders on the same volume, so this is a
        // rename: the library only ever sees the complete file appear.
        private static string Publish(string stagedPath, string outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Move(stagedPath, outputPath, overwrite: true);
            return outputPath;
        }

        private string CreateStagingDirectory()
        {
            var path = Path.Combine(_workingDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Self-contained Emby builds (e.g. linuxserver/emby) run with LD_LIBRARY_PATH pointing at
        /// their own bundled lib directory, whose libc is often older than the host's, so a binary
        /// spawned from the Emby process (yt-dlp, or /usr/bin/env via its shebang) can fail with a
        /// "GLIBC_x.xx not found" error. Prepending the host's standard library dirs makes the
        /// dynamic linker find the newer system glibc first - safe because glibc is backwards
        /// compatible.
        /// </summary>
        private static void PreferSystemDynamicLinker(ProcessStartInfo startInfo)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            const string systemLibPaths =
                "/lib:/usr/lib:/lib/x86_64-linux-gnu:/usr/lib/x86_64-linux-gnu:/lib/aarch64-linux-gnu:/usr/lib/aarch64-linux-gnu";

            var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            startInfo.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existing)
                ? systemLibPaths
                : systemLibPaths + ":" + existing;
        }

        private static bool HasUsableFfmpeg(string ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                return false;
            }

            if (Path.IsPathRooted(ffmpegPath))
            {
                return File.Exists(ffmpegPath);
            }

            var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ffmpegPath + ".exe" : ffmpegPath;
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return pathVar.Split(Path.PathSeparator)
                .Where(dir => !string.IsNullOrWhiteSpace(dir))
                .Any(dir => File.Exists(Path.Combine(dir, exe)));
        }

        private void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not stop timed-out yt-dlp process");
            }
        }

        // Also removes the separate trailer.fNNN.mp4 / .m4a streams a failed merge leaves behind,
        // so a later refresh retries cleanly and they don't pile up on disk.
        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not clean up trailer staging folder {Path}", path);
            }
        }
    }
}
