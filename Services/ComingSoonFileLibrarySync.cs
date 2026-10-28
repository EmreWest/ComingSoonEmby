using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ComingSoonPlugin.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Writes each item in one list as a real folder (poster.jpg, a playable video, and a
    /// Kodi/Emby-style .nfo) under that list's own data path, and points one normal Emby library
    /// at it so Emby's own file scanner indexes it like any other media. Directly injecting
    /// BaseItem rows via ILibraryManager never worked reliably; real files on disk is the
    /// supported path (see github.com/mrt187/release-poster-sync).
    /// </summary>
    public class ComingSoonFileLibrarySync
    {
        public const string MainVideoFileName = "video.mp4";

        private const string DummyVideoResourceName = "ComingSoonPlugin.Assets.dummy.mp4";

        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly string _libraryName;
        private readonly string _libraryDataPath;
        private readonly ComingSoonLanguage _language;

        public ComingSoonFileLibrarySync(
            ILibraryManager libraryManager, IProviderManager providerManager, IFileSystem fileSystem,
            ILogger logger, string libraryName, string libraryDataPath,
            ComingSoonLanguage language = ComingSoonLanguage.English)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _logger = logger;
            _libraryName = libraryName;
            _libraryDataPath = libraryDataPath;
            _language = language;
        }

        public void Sync(
            List<CachedMediaItem> items,
            bool forceImageRefresh = false,
            bool removeStaleMovies = true,
            bool removeStaleEpisodes = true)
        {
            Directory.CreateDirectory(_libraryDataPath);
            EnsureLibraryRegistered();

            var expectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var folderName = BuildFolderName(item);
                expectedFolders.Add(folderName);

                try
                {
                    WriteEntry(item, folderName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Coming Soon: failed to write library entry for '{Title}'", item.Title);
                }
            }

            CleanupStaleFolders(expectedFolders, removeStaleMovies, removeStaleEpisodes);
            _libraryManager.QueueLibraryScan();

            if (forceImageRefresh)
            {
                ForceItemRefresh(expectedFolders);
            }

            _logger.LogInformation(
                "Coming Soon: file library sync complete - {Count} entries at {Path}, scan queued",
                expectedFolders.Count, _libraryDataPath);
        }

        /// <summary>
        /// Queues a forced image+metadata refresh for each entry that already exists in Emby's DB.
        /// A plain library scan only notices added/removed files, not changed image content or a
        /// changed preferred language, so after a language switch the re-rendered badges and the
        /// overview (now fetched in the new language, see BuildLibraryOptions) would otherwise keep
        /// showing the old language until the library was deleted and recreated. This makes a
        /// switch take effect on the next refresh instead.
        /// </summary>
        private void ForceItemRefresh(HashSet<string> expectedFolders)
        {
            foreach (var folderName in expectedFolders)
            {
                var entryDir = Path.Combine(_libraryDataPath, folderName);
                var item = _libraryManager.FindByPath(entryDir, true);
                if (item is null)
                {
                    continue;
                }

                _providerManager.QueueRefresh(
                    item.InternalId,
                    new MetadataRefreshOptions(_fileSystem)
                    {
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                        ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                        ReplaceAllImages = true
                    },
                    RefreshPriority.Normal);
            }
        }

        private void EnsureLibraryRegistered()
        {
            var existingInfo = _libraryManager.GetVirtualFolders()
                .FirstOrDefault(f => string.Equals(f.Name, _libraryName, StringComparison.OrdinalIgnoreCase));

            if (existingInfo is not null)
            {
                // VirtualFolderInfo has three id-shaped fields; only one round-trips through
                // GetItemById, so try all of them.
                var existingFolder = new[] { existingInfo.Guid, existingInfo.Id, existingInfo.ItemId }
                    .Select(candidate => Guid.TryParse(candidate, out var id) ? _libraryManager.GetItemById(id) as CollectionFolder : null)
                    .FirstOrDefault(f => f is not null);

                // Re-applied every sync (not just on first creation) so libraries created before
                // this restriction existed also pick it up, and so it survives an admin resetting
                // it via the Dashboard.
                existingFolder?.UpdateLibraryOptions(BuildLibraryOptions());

                var alreadyHasPath = existingInfo.Locations?.Any(
                    l => string.Equals(l, _libraryDataPath, StringComparison.OrdinalIgnoreCase)) == true;

                if (!alreadyHasPath && existingFolder is not null)
                {
                    _libraryManager.AddMediaPaths(
                        existingFolder, new[] { new MediaPathInfo { Path = _libraryDataPath } }, refreshLibrary: false);
                }

                if (alreadyHasPath || existingFolder is not null)
                {
                    return;
                }
            }

            var folder = _libraryManager.AddVirtualFolder(_libraryName, BuildLibraryOptions(), refreshLibrary: false);
            _libraryManager.AddMediaPaths(folder, new[] { new MediaPathInfo { Path = _libraryDataPath } }, refreshLibrary: false);
        }

        /// <summary>
        /// Realtime monitoring is on so Emby picks up added, changed and removed entries as soon
        /// as a refresh writes them, instead of waiting for its next scheduled library scan.
        /// Trailers are staged outside the library (see TrailerDownloader) so the monitor never
        /// sees a half-written video.
        /// Blocks Emby's own online image fetchers for this library so poster.jpg/backdrop.jpg -
        /// which already carry our badge overlay - can never be silently replaced by an unbadged
        /// copy on Emby's own periodic library scan. Metadata fetchers are left at Emby's default
        /// so cast/crew autofill (see BuildNfo) keeps working for users without a TMDB key.
        /// PreferredMetadataLanguage pins this library to the plugin's language, so the overview
        /// Emby fetches itself matches the badges instead of following the global server language.
        /// </summary>
        private LibraryOptions BuildLibraryOptions() => new()
        {
            EnableRealtimeMonitor = true,
            SaveLocalMetadata = false,
            PreferredMetadataLanguage = _language == ComingSoonLanguage.German ? "de" : "en",
            MetadataCountryCode = _language == ComingSoonLanguage.German ? "DE" : "US",
            TypeOptions = new[]
            {
                new TypeOptions { Type = "Movie", ImageFetchers = Array.Empty<string>() },
                new TypeOptions { Type = "Series", ImageFetchers = Array.Empty<string>() },
                new TypeOptions { Type = "Episode", ImageFetchers = Array.Empty<string>() }
            }
        };

        private static string BuildFolderName(CachedMediaItem item) =>
            ComingSoonEntryPaths.BuildFolderName(
                item.Title, item.Type, item.SeasonNumber, item.EpisodeNumber, item.TmdbId, item.TvdbId);

        private void WriteEntry(CachedMediaItem item, string folderName)
        {
            // poster.jpg/backdrop.jpg and the downloaded trailer are already written by
            // RefreshComingSoonTask.EnrichAsync. Emby needs a playable main file for the entry to
            // show up at all: either the trailer itself (TrailerIsMainVideo) or the short bundled
            // placeholder, with the trailer then living in trailers/ as Emby's "Trailer" button.
            var entryDir = Path.Combine(_libraryDataPath, folderName);
            Directory.CreateDirectory(entryDir);

            // Rewrite the placeholder only when it's missing or the main video is a trailer that
            // is no longer wanted there - keeps the file, and thus Emby's scan, stable when
            // nothing changed.
            var videoPath = Path.Combine(entryDir, MainVideoFileName);
            var dummy = GetDummyVideoBytes();
            if (!item.TrailerIsMainVideo && dummy is not null && !IsPlaceholderVideo(videoPath))
            {
                File.WriteAllBytes(videoPath, dummy);
            }

            File.WriteAllText(Path.Combine(entryDir, "movie.nfo"), BuildNfo(item, _language));
        }

        /// <summary>True if path exists and is the bundled placeholder rather than a real trailer.</summary>
        public static bool IsPlaceholderVideo(string path)
        {
            var dummy = GetDummyVideoBytes();
            return dummy is not null && File.Exists(path) && new FileInfo(path).Length == dummy.Length;
        }

        private static byte[]? _dummyVideoBytes;

        private static byte[]? GetDummyVideoBytes()
        {
            if (_dummyVideoBytes is not null)
            {
                return _dummyVideoBytes;
            }

            using var resourceStream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(DummyVideoResourceName);
            if (resourceStream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            resourceStream.CopyTo(memory);
            _dummyVideoBytes = memory.ToArray();
            return _dummyVideoBytes;
        }

        private static string BuildNfo(CachedMediaItem item, ComingSoonLanguage language)
        {
            var title = item.Type == CalendarItemType.Episode
                ? $"{item.Title} - S{item.SeasonNumber:D2}E{item.EpisodeNumber:D2}"
                    + (string.IsNullOrWhiteSpace(item.EpisodeTitle) ? string.Empty : $" - {item.EpisodeTitle}")
                : item.Title;

            string tagline;
            string defaultPlot;
            if (language == ComingSoonLanguage.German)
            {
                var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                tagline = $"Ab {item.Date.ToString("d. MMM yyyy", de)}";
                defaultPlot = "Demnächst verfügbar.";
            }
            else
            {
                var en = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                tagline = $"Coming {item.Date.ToString("MMM d, yyyy", en)}";
                defaultPlot = "Coming soon.";
            }

            // <movie> root even for series entries - the S/E badge is already burned into the
            // poster, so a full Series/Season/Episode NFO hierarchy isn't needed here. Episode
            // cards are locked because their series-level metadata was already embedded above;
            // allowing Emby to identify these fake movies would assign the same parent-series
            // TVDB id to every episode and collapse them into a single home-screen card.
            var root = new XElement(
                "movie",
                new XElement("title", title),
                new XElement("premiered", item.Date.ToString("yyyy-MM-dd")),
                new XElement("tagline", tagline),
                new XElement("plot", string.IsNullOrWhiteSpace(item.Overview) ? defaultPlot : item.Overview),
                new XElement("genre", "Coming Soon"),
                new XElement("lockdata", item.Type == CalendarItemType.Episode ? "true" : "false"));

            if (item.TmdbId.HasValue)
            {
                root.Add(new XElement("uniqueid", new XAttribute("type", "tmdb"), item.TmdbId.Value));
            }

            // TvdbId on CalendarItem identifies the parent series, not the individual episode.
            // Never write it as this movie-shaped entry's provider id; Emby derives its
            // PresentationUniqueKey from provider ids and would treat all episodes as versions
            // of the same item.

            foreach (var castMember in item.Cast)
            {
                var actor = new XElement(
                    "actor",
                    new XElement("name", castMember.Name),
                    new XElement("role", castMember.Character));

                if (!string.IsNullOrWhiteSpace(castMember.ProfileImageUrl))
                {
                    actor.Add(new XElement("thumb", castMember.ProfileImageUrl));
                }

                root.Add(actor);
            }

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + root;
        }

        private void CleanupStaleFolders(
            HashSet<string> expectedFolders, bool removeStaleMovies, bool removeStaleEpisodes)
        {
            if (!Directory.Exists(_libraryDataPath))
            {
                return;
            }

            foreach (var dir in Directory.EnumerateDirectories(_libraryDataPath))
            {
                var name = Path.GetFileName(dir);
                if (expectedFolders.Contains(name))
                {
                    continue;
                }

                // A failed calendar request is not an empty calendar. Preserve entries belonging
                // to that source until a later refresh successfully confirms that they are stale.
                var isMovie = name.Contains("[tmdb-", StringComparison.OrdinalIgnoreCase);
                var isEpisode = name.Contains("[tvdb-", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("[comingsoon-series-", StringComparison.OrdinalIgnoreCase);
                if ((isMovie && !removeStaleMovies) || (isEpisode && !removeStaleEpisodes))
                {
                    _logger.LogDebug("Coming Soon: preserving {Name} because its source fetch failed", name);
                    continue;
                }

                try
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogDebug("Coming Soon: removed stale entry folder {Name}", name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Coming Soon: failed to remove stale entry folder {Name}", name);
                }
            }
        }
    }
}
