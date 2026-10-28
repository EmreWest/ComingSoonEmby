using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace ComingSoonPlugin
{
    /// <summary>Which Radarr release-date field to treat as "the" date for a movie.</summary>
    public enum RadarrReleaseDateType
    {
        Digital,
        Physical,
        InCinemas,
        Earliest
    }

    /// <summary>Language for poster badge text and fetched Overview/plot text.</summary>
    public enum ComingSoonLanguage
    {
        English,
        German
    }

    /// <summary>What the top status badge says, derived from a list's release-date type.</summary>
    public enum ReleaseBadgeKind
    {
        Upcoming,
        InCinemas,
        OnDisc,
        Digital
    }

    /// <summary>Which corner of the poster/backdrop the status badge sits in.</summary>
    public enum BadgeCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// One admin-configured home-screen row: its own Emby library, synced independently every
    /// refresh. A movie can appear in more than one list if it matches more than one.
    /// </summary>
    public class ComingSoonListConfig
    {
        public string Name { get; set; } = "Coming Soon";
        public bool IncludeMovies { get; set; } = true;
        public RadarrReleaseDateType MovieDateType { get; set; } = RadarrReleaseDateType.Digital;
        /// <summary>ISO 3166-1 country used for regional TMDB movie release dates.</summary>
        public string ReleaseDateRegion { get; set; } = "DE";
        public bool IncludeTvShows { get; set; } = true;
    }

    /// <summary>One Sonarr or Radarr server. Several of each can be configured.</summary>
    public class ArrInstanceConfig
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Trailer languages for this server's titles, as comma-separated ISO 639-1 codes in order
        /// of preference (e.g. "ja" for an anime Sonarr). Empty uses the plugin language. English
        /// and then the title's original language are always tried before giving up.
        /// </summary>
        public string TrailerLanguages { get; set; } = string.Empty;

        /// <summary>
        /// Uploaded (never automatic) YouTube subtitles to save with the trailer, as comma-separated
        /// ISO 639-1 codes (e.g. "de"). Empty saves none.
        /// </summary>
        public string SubtitleLanguages { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Url : Name;

        /// <summary>Parses "de, ja;EN" into ["de", "ja", "en"], dropping anything that isn't a language code.</summary>
        public static List<string> ParseLanguages(string? value) =>
            (value ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.ToLowerInvariant())
                .Where(l => l.Length is 2 or 3 && l.All(char.IsAsciiLetter))
                .Distinct()
                .ToList();
    }

    /// <summary>Persisted plugin settings, edited via the Dashboard config page.</summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Pre-1.3 single-server settings. Only read while the matching *Instances list is empty,
        // so an existing setup keeps working until the config page saves it as an instance list.
        public bool SonarrEnabled { get; set; } = false;
        public string SonarrUrl { get; set; } = "http://localhost:8989";
        public string SonarrApiKey { get; set; } = string.Empty;

        public bool RadarrEnabled { get; set; } = false;
        public string RadarrUrl { get; set; } = "http://localhost:7878";
        public string RadarrApiKey { get; set; } = string.Empty;

        /// <summary>Initialised empty for the same XmlSerializer reason as <see cref="Lists"/>.</summary>
        public List<ArrInstanceConfig> SonarrInstances { get; set; } = new();

        /// <summary>Initialised empty for the same XmlSerializer reason as <see cref="Lists"/>.</summary>
        public List<ArrInstanceConfig> RadarrInstances { get; set; } = new();

        public List<ArrInstanceConfig> GetSonarrInstances() =>
            ResolveInstances(SonarrInstances, SonarrEnabled, SonarrUrl, SonarrApiKey, "Sonarr");

        public List<ArrInstanceConfig> GetRadarrInstances() =>
            ResolveInstances(RadarrInstances, RadarrEnabled, RadarrUrl, RadarrApiKey, "Radarr");

        private static List<ArrInstanceConfig> ResolveInstances(
            List<ArrInstanceConfig>? instances, bool legacyEnabled, string legacyUrl, string legacyApiKey, string legacyName)
        {
            if (instances is { Count: > 0 })
            {
                return instances;
            }

            return legacyEnabled
                ? new List<ArrInstanceConfig> { new() { Name = legacyName, Url = legacyUrl, ApiKey = legacyApiKey } }
                : new List<ArrInstanceConfig>();
        }

        /// <summary>
        /// Initialised empty on purpose: the XmlSerializer Emby uses *appends* deserialised
        /// elements to whatever the getter already holds (it ignores the setter for collections),
        /// so a non-empty default would grow the list by its default entries on every plugin load.
        /// A missing/empty list is treated as one default row by the task and the config page.
        /// </summary>
        public List<ComingSoonListConfig> Lists { get; set; } = new();

        /// <summary>Optional - used for regional movie dates, trailer download and metadata fallback.</summary>
        public string TmdbApiKey { get; set; } = string.Empty;

        /// <summary>Badge text and fetched Overview/plot language.</summary>
        public ComingSoonLanguage Language { get; set; } = ComingSoonLanguage.English;

        /// <summary>Which corner the status badge (UPCOMING / IN CINEMAS / ...) is drawn in.</summary>
        public BadgeCorner BadgePosition { get; set; } = BadgeCorner.TopLeft;

        /// <summary>
        /// The Language value the last refresh actually rendered with, set by the plugin itself
        /// (not the config page). Lets the next refresh detect a language change and force Emby to
        /// re-read the re-badged local images, so a language switch takes effect without deleting
        /// and recreating the libraries.
        /// </summary>
        public ComingSoonLanguage LastSyncedLanguage { get; set; } = ComingSoonLanguage.English;

        /// <summary>
        /// Badge-appearance version the last refresh rendered with, set by the plugin. When the
        /// badge design changes (see RefreshComingSoonTask.BadgeRenderVersion) this differs from
        /// the current version, so the next refresh forces Emby to re-read the re-rendered images.
        /// </summary>
        public int LastBadgeRenderVersion { get; set; } = 0;

        /// <summary>How many days ahead to pull from the Sonarr/Radarr calendars.</summary>
        public int DaysAhead { get; set; } = 30;

        /// <summary>Download trailers locally via yt-dlp instead of only caching the YouTube key.</summary>
        public bool DownloadTrailersLocally { get; set; } = true;

        /// <summary>
        /// Use the downloaded trailer as the entry's main video, so pressing Play starts the
        /// trailer. When off, the trailer is a separate extra (Emby's "Trailer" button) and the
        /// main video stays the short placeholder. Entries without a trailer always keep the
        /// placeholder.
        /// </summary>
        public bool TrailerAsMainVideo { get; set; } = true;

        /// <summary>
        /// Path to a yt-dlp executable/binary. Default "yt-dlp" tries PATH first, then falls back
        /// to auto-downloading a standalone copy into the plugin's own data folder if not found.
        /// </summary>
        public string YtDlpPath { get; set; } = "yt-dlp";

        /// <summary>
        /// JavaScript runtime yt-dlp uses for YouTube. Empty (default) uses deno from PATH or
        /// auto-downloads it into the plugin's data folder. A path is treated as a deno binary;
        /// "node:/path", "bun:/path" or "quickjs:/path" select another runtime.
        /// </summary>
        public string JsRuntimePath { get; set; } = string.Empty;

        /// <summary>
        /// Path to an ffmpeg executable, used by yt-dlp for remuxing. Empty (default) uses
        /// whatever ffmpeg Emby itself is already configured to use.
        /// </summary>
        public string FfmpegPath { get; set; } = string.Empty;
    }
}
