using System.Collections.Generic;

namespace ComingSoonPlugin.Models
{
    /// <summary>
    /// Enrichment payload returned by either EmbyProviderMetadataService (primary) or
    /// TmdbService (fallback / trailer lookup), merged onto a CalendarItem to produce
    /// a CachedMediaItem.
    /// </summary>
    public class MetadataResult
    {
        public string? Overview { get; set; }
        public string? PosterUrl { get; set; }
        public string? BackdropUrl { get; set; }
        public List<CastMember> Cast { get; set; } = new();
        public string? TrailerYoutubeKey { get; set; }
        public string? TrailerLanguage { get; set; }
    }
}
