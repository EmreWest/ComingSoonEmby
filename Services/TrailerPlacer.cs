using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Puts each entry's trailer, and its subtitles, where the current settings want it: as the
    /// main video, or as trailers/trailer.mp4 next to the placeholder. One instance is used per
    /// refresh, so a trailer shared by several entries (every episode of a series, a movie in
    /// several lists) is downloaded once and copied to the others.
    /// </summary>
    public class TrailerPlacer
    {
        // Records which YouTube video an entry's trailer is and which subtitle languages were
        // already tried for it, so a different trailer choice (e.g. after changing the trailer
        // language) replaces the file, and missing subtitles aren't requested again every refresh.
        // Hidden and without a media extension, so Emby ignores it.
        private const string MarkerFileName = ".trailer-source";

        private readonly TrailerDownloader _downloader;
        private readonly ILogger _logger;
        private readonly Dictionary<string, Marker> _placedThisRun = new();

        public TrailerPlacer(TrailerDownloader downloader, ILogger logger)
        {
            _downloader = downloader;
            _logger = logger;
        }

        /// <summary>
        /// Returns true if the entry's main video is now the trailer, in which case the library
        /// sync must not overwrite it with the placeholder. A null trailerKey (no trailer found
        /// this time) keeps whatever trailer the entry already has.
        /// </summary>
        public async Task<bool> PlaceAsync(
            string? trailerKey,
            IReadOnlyCollection<string> subtitleLanguages,
            string entryDir,
            bool asMainVideo,
            CancellationToken cancellationToken)
        {
            var mainVideoPath = Path.Combine(entryDir, ComingSoonFileLibrarySync.MainVideoFileName);
            var extraDir = Path.Combine(entryDir, "trailers");
            var extraPath = Path.Combine(extraDir, TrailerDownloader.TrailerFileName);
            var targetPath = asMainVideo ? mainVideoPath : extraPath;
            var mainIsTrailer = File.Exists(mainVideoPath) && !ComingSoonFileLibrarySync.IsPlaceholderVideo(mainVideoPath);

            // The setting changed since the last refresh: move the trailer instead of downloading
            // it again. Moving the main video away lets the library sync write the placeholder.
            if (asMainVideo && !mainIsTrailer && File.Exists(extraPath))
            {
                MoveTrailer(extraPath, mainVideoPath);
                TryDeleteEmptyDirectory(extraDir);
                mainIsTrailer = true;
            }
            else if (!asMainVideo && mainIsTrailer && !File.Exists(extraPath))
            {
                Directory.CreateDirectory(extraDir);
                MoveTrailer(mainVideoPath, extraPath);
                mainIsTrailer = false;
            }

            var present = asMainVideo ? mainIsTrailer : File.Exists(extraPath);
            var marker = ReadMarker(entryDir);
            var markerChanged = false;

            // Trailers from before the marker existed count as different, so they're chosen
            // again once under the current language settings.
            if (trailerKey is not null && (!present || marker?.Key != trailerKey))
            {
                var placed = ReusePlaced(trailerKey, targetPath)
                    ?? await DownloadVideoAsync(trailerKey, targetPath, cancellationToken).ConfigureAwait(false);

                if (placed is not null)
                {
                    if (present)
                    {
                        _logger.LogInformation("Coming Soon: replaced trailer in {Entry} with {Key}", entryDir, trailerKey);
                    }

                    marker = placed;
                    markerChanged = true;
                    present = true;
                }
            }

            if (!present)
            {
                return false;
            }

            if (marker is not null)
            {
                var missing = subtitleLanguages.Where(l => !marker.TriedSubtitles.Contains(l)).ToList();
                if (missing.Count > 0)
                {
                    var saved = await _downloader.DownloadSubtitlesAsync(marker.Key, missing, targetPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (saved is not null)
                    {
                        marker.TriedSubtitles.UnionWith(missing);
                        markerChanged = true;
                        if (saved.Count < missing.Count)
                        {
                            _logger.LogInformation(
                                "Coming Soon: trailer {Key} has no uploaded subtitles in {Languages}",
                                marker.Key, string.Join(", ", missing.Except(saved)));
                        }
                    }
                }

                if (markerChanged)
                {
                    WriteMarker(entryDir, marker);
                }

                _placedThisRun.TryAdd(marker.Key, new Marker(marker.Key, NewLanguageSet(marker.TriedSubtitles), targetPath));
            }

            return asMainVideo;
        }

        // Copies a trailer already placed for another entry this run, with its subtitles.
        private Marker? ReusePlaced(string trailerKey, string targetPath)
        {
            if (!_placedThisRun.TryGetValue(trailerKey, out var source) || source.VideoPath is null
                || !File.Exists(source.VideoPath) || _downloader.ReuseExisting(source.VideoPath, targetPath) is null)
            {
                return null;
            }

            DeleteSubtitles(targetPath);
            foreach (var subtitle in SubtitleFiles(source.VideoPath))
            {
                File.Copy(subtitle, RenameSubtitle(subtitle, source.VideoPath, targetPath), overwrite: true);
            }

            return new Marker(trailerKey, NewLanguageSet(source.TriedSubtitles));
        }

        private async Task<Marker?> DownloadVideoAsync(string trailerKey, string targetPath, CancellationToken cancellationToken)
        {
            if (await _downloader.DownloadAsync(trailerKey, targetPath, cancellationToken).ConfigureAwait(false) is null)
            {
                return null;
            }

            // Subtitles of a previous trailer don't belong to this one.
            DeleteSubtitles(targetPath);
            return new Marker(trailerKey, NewLanguageSet(Array.Empty<string>()));
        }

        private static void MoveTrailer(string fromVideo, string toVideo)
        {
            File.Move(fromVideo, toVideo, overwrite: true);
            foreach (var subtitle in SubtitleFiles(fromVideo))
            {
                File.Move(subtitle, RenameSubtitle(subtitle, fromVideo, toVideo), overwrite: true);
            }
        }

        // "video.de.vtt" belongs to "video.mp4".
        private static IEnumerable<string> SubtitleFiles(string videoPath)
        {
            var directory = Path.GetDirectoryName(videoPath)!;
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, Path.GetFileNameWithoutExtension(videoPath) + ".*.vtt")
                : Array.Empty<string>();
        }

        private static string RenameSubtitle(string subtitle, string fromVideo, string toVideo)
        {
            var suffix = Path.GetFileName(subtitle).Substring(Path.GetFileNameWithoutExtension(fromVideo).Length);
            return Path.Combine(Path.GetDirectoryName(toVideo)!, Path.GetFileNameWithoutExtension(toVideo) + suffix);
        }

        private static void DeleteSubtitles(string videoPath)
        {
            foreach (var subtitle in SubtitleFiles(videoPath))
            {
                File.Delete(subtitle);
            }
        }

        private static Marker? ReadMarker(string entryDir)
        {
            var path = Path.Combine(entryDir, MarkerFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                return null;
            }

            var tried = lines.Length > 1
                ? lines[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>();
            return new Marker(lines[0].Trim(), NewLanguageSet(tried));
        }

        private static void WriteMarker(string entryDir, Marker marker) =>
            File.WriteAllLines(
                Path.Combine(entryDir, MarkerFileName),
                new[] { marker.Key, string.Join(",", marker.TriedSubtitles.OrderBy(l => l)) });

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                }
            }
            catch (Exception)
            {
                // Harmless leftover; the next refresh tries again.
            }
        }

        private static HashSet<string> NewLanguageSet(IEnumerable<string> languages) =>
            new(languages, StringComparer.OrdinalIgnoreCase);

        private sealed record Marker(string Key, HashSet<string> TriedSubtitles, string? VideoPath = null);
    }
}
