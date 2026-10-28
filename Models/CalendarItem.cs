using System;

namespace ComingSoonPlugin.Models
{
    public enum CalendarItemType
    {
        Movie,
        Episode
    }

    /// <summary>
    /// Normalized shape for a single Sonarr or Radarr calendar entry, before any
    /// TMDB/provider/trailer enrichment has happened.
    /// </summary>
    public class CalendarItem
    {
        public CalendarItemType Type { get; set; }

        /// <summary>Movie title, or parent series title for episodes.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Resolved date used for display/sorting/window-filtering. For episodes this is
        /// airDateUtc; for movies it's resolved per-list from the raw dates below (see
        /// RadarrService.ResolveDate), since different lists can use different date types.
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>Shown beside the date when a regional fallback (currently US) was used.</summary>
        public string? DateRegionLabel { get; set; }

        public string Overview { get; set; } = string.Empty;

        public int? TmdbId { get; set; }

        public int? TvdbId { get; set; }

        // --- Movie-only: raw Radarr dates, resolved per-list via RadarrService.ResolveDate. ---
        public DateTime? InCinemasDate { get; set; }
        public DateTime? PhysicalReleaseDate { get; set; }
        public DateTime? DigitalReleaseDate { get; set; }

        // --- Episode-only fields ---
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }

        /// <summary>Poster URL as returned directly by Sonarr/Radarr, if any (fallback if TMDB/provider lookup fails).</summary>
        public string? PosterUrl { get; set; }

        /// <summary>YouTube trailer id as returned by Radarr, used when TMDB has none (or no TMDB key is set).</summary>
        public string? TrailerYoutubeKey { get; set; }

        /// <summary>Shallow copy - used to give the same underlying movie a different resolved Date per list.</summary>
        public CalendarItem Clone() => (CalendarItem)MemberwiseClone();

        /// <summary>
        /// Identity key independent of the resolved Date, so metadata (overview/cast/poster/
        /// trailer) fetched for this item can be cached and reused across lists that both
        /// include the same movie/episode with a different resolved date.
        /// </summary>
        public string StableKey => TmdbId.HasValue ? $"tmdb-{TmdbId}" : $"tvdb-{TvdbId}-s{SeasonNumber}e{EpisodeNumber}";

        /// <summary>
        /// Cache key for fetched metadata. For episodes this is the series (tvdbId) without the
        /// season/episode, because overview/cast/trailer all come from TMDB at the series level -
        /// so every episode of a series shares one lookup and one trailer instead of searching
        /// per episode.
        /// </summary>
        public string MetadataCacheKey => TmdbId.HasValue ? $"tmdb-{TmdbId}" : $"tvdb-{TvdbId}";
    }
}
