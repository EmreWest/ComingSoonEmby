using System;
using System.Collections.Generic;

namespace ComingSoonPlugin.Models
{
    public class CastMember
    {
        public string Name { get; set; } = string.Empty;
        public string Character { get; set; } = string.Empty;
        public string? ProfileImageUrl { get; set; }
    }

    /// <summary>
    /// Fully enriched item, one per calendar entry, after merging Sonarr/Radarr + Emby provider
    /// (or TMDB fallback). Written to disk by ComingSoonFileLibrarySync. Poster/backdrop/trailer
    /// files are written separately during enrichment, so their paths aren't carried here.
    /// </summary>
    public class CachedMediaItem
    {
        public CalendarItemType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Overview { get; set; } = string.Empty;

        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }

        /// <summary>The entry's main video is the downloaded trailer, not the bundled placeholder.</summary>
        public bool TrailerIsMainVideo { get; set; }

        public List<CastMember> Cast { get; set; } = new();
    }
}
