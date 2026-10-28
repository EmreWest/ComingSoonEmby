using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComingSoonPlugin.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>Poster and backdrop of a matching item in the user's own Emby libraries.</summary>
    public sealed record LibraryArtwork(string? Poster, string? Backdrop);

    /// <summary>
    /// Finds a title that already exists in one of the user's Emby libraries - typically the
    /// series of an upcoming episode - by its TVDB/TMDB id, so the Coming Soon entry can reuse its
    /// poster and backdrop: the artwork the user already picked, in their language. The Coming
    /// Soon libraries themselves are skipped, since their movie entries carry TMDB ids too.
    /// </summary>
    public class EmbyLibraryArtworkService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly string _ownDataPath;
        private readonly Dictionary<string, LibraryArtwork?> _cache = new();

        public EmbyLibraryArtworkService(ILibraryManager libraryManager, ILogger logger, string ownDataPath)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _ownDataPath = ownDataPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Returns the artwork of the matching series (for episodes) or movie, or null if the
        /// title isn't in any library. Poster/Backdrop are local file paths or http(s) URLs.
        /// </summary>
        public LibraryArtwork? Find(CalendarItem item)
        {
            var (type, provider, id) = item.Type == CalendarItemType.Episode
                ? ("Series", "Tvdb", item.TvdbId)
                : ("Movie", "Tmdb", item.TmdbId);
            if (!id.HasValue)
            {
                return null;
            }

            var cacheKey = $"{type}:{id}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            LibraryArtwork? artwork = null;
            try
            {
                var match = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { type },
                        Recursive = true,
                        AnyProviderIdEquals = new List<KeyValuePair<string, string>>
                        {
                            new(provider, id.Value.ToString())
                        }
                    })
                    .FirstOrDefault(i => !IsOwnItem(i) && ImageSource(i, ImageType.Primary) is not null);

                if (match is not null)
                {
                    artwork = new LibraryArtwork(ImageSource(match, ImageType.Primary), ImageSource(match, ImageType.Backdrop));
                    _logger.LogDebug("Coming Soon: using artwork of library item '{Name}' for '{Title}'", match.Name, item.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Coming Soon: library artwork lookup failed for '{Title}'", item.Title);
            }

            _cache[cacheKey] = artwork;
            return artwork;
        }

        private bool IsOwnItem(BaseItem item) =>
            !string.IsNullOrEmpty(item.Path) && item.Path.StartsWith(_ownDataPath, StringComparison.OrdinalIgnoreCase);

        private static string? ImageSource(BaseItem item, ImageType type)
        {
            var image = item.GetImageInfo(type, 0);
            if (image is null || string.IsNullOrEmpty(image.Path))
            {
                return null;
            }

            if (image.IsLocalFile)
            {
                return File.Exists(image.Path) ? image.Path : null;
            }

            return image.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? image.Path : null;
        }
    }
}
