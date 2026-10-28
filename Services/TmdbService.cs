using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ComingSoonPlugin.Models;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Direct TMDB v3 API client - the trailer-key source for both movies and series, and a
    /// fallback for Overview/Cast/Poster. Optional: PluginConfiguration.TmdbApiKey may be empty.
    /// </summary>
    public class TmdbService
    {
        private const string BaseUrl = "https://api.themoviedb.org/3";

        private const string ImageBaseUrl = "https://image.tmdb.org/t/p/w500";
        private const string BackdropImageBaseUrl = "https://image.tmdb.org/t/p/w1280";

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string _apiKey;
        private readonly string _language;

        public TmdbService(HttpClient httpClient, ILogger logger, string apiKey, ComingSoonLanguage language = ComingSoonLanguage.English)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = apiKey;
            _language = language == ComingSoonLanguage.German ? "de-DE" : "en-US";
        }

        private bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<MetadataResult?> GetMovieMetadataAsync(
            int tmdbId, IReadOnlyList<string> trailerLanguages, IReadOnlyList<string> posterLanguages,
            CancellationToken cancellationToken)
        {
            if (!HasApiKey)
            {
                return null;
            }

            return await FetchAndMapAsync("movie", tmdbId, trailerLanguages, posterLanguages, cancellationToken).ConfigureAwait(false);
        }

        public async Task<MetadataResult?> GetSeriesMetadataAsync(
            int tmdbTvId, IReadOnlyList<string> trailerLanguages, IReadOnlyList<string> posterLanguages,
            CancellationToken cancellationToken)
        {
            if (!HasApiKey)
            {
                return null;
            }

            return await FetchAndMapAsync("tv", tmdbTvId, trailerLanguages, posterLanguages, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the earliest relevant movie dates for one country. TMDB release types are:
        /// 2=limited theatrical, 3=theatrical, 4=digital and 5=physical. A regular theatrical
        /// date is preferred; limited theatrical is only used when no regular date exists.
        /// </summary>
        public async Task<RegionalReleaseDates?> GetMovieReleaseDatesAsync(
            int tmdbId, string countryCode, CancellationToken cancellationToken)
        {
            if (!HasApiKey)
            {
                return null;
            }

            var region = NormalizeCountryCode(countryCode);
            var url = $"{BaseUrl}/movie/{tmdbId}/release_dates?api_key={_apiKey}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "TMDB release-date lookup failed for movie {TmdbId}, region {Region}: {Status}",
                        tmdbId, region, response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<TmdbReleaseDatesResponse>(json, JsonOptions);
                var country = result?.Results?.FirstOrDefault(r =>
                    string.Equals(r.CountryCode, region, StringComparison.OrdinalIgnoreCase));

                if (country?.ReleaseDates is not { Count: > 0 })
                {
                    _logger.LogDebug(
                        "TMDB has no release dates for movie {TmdbId} in region {Region}",
                        tmdbId, region);
                    return new RegionalReleaseDates();
                }

                var regularCinema = EarliestDate(country.ReleaseDates, 3);
                var limitedCinema = EarliestDate(country.ReleaseDates, 2);

                return new RegionalReleaseDates
                {
                    InCinemas = regularCinema ?? limitedCinema,
                    Digital = EarliestDate(country.ReleaseDates, 4),
                    Physical = EarliestDate(country.ReleaseDates, 5)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "TMDB release-date lookup failed for movie {TmdbId}, region {Region}; Radarr dates will be used",
                    tmdbId, region);
                return null;
            }
        }

        private static DateTime? EarliestDate(IEnumerable<TmdbReleaseDateDto> dates, int type) =>
            dates.Where(d => d.Type == type && d.ReleaseDate.HasValue)
                .Select(d => d.ReleaseDate!.Value.Date)
                .Cast<DateTime?>()
                .Min();

        private static string NormalizeCountryCode(string? countryCode)
        {
            var normalized = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
            return normalized.Length == 2 && normalized.All(char.IsLetter) ? normalized : "DE";
        }

        /// <summary>
        /// Sonarr only gives us a tvdbId, but TMDB's /tv endpoints are keyed by TMDB's own id.
        /// This bridges the gap via TMDB's /find endpoint. Returns null if TMDB has no match.
        /// </summary>
        public async Task<int?> FindTmdbTvIdByTvdbIdAsync(int tvdbId, CancellationToken cancellationToken)
        {
            if (!HasApiKey)
            {
                return null;
            }

            var url = $"{BaseUrl}/find/{tvdbId}?api_key={_apiKey}&external_source=tvdb_id";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB /find lookup failed for tvdbId {TvdbId}: {Status}", tvdbId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<TmdbFindResponse>(json, JsonOptions);

            return result?.TvResults?.FirstOrDefault()?.Id;
        }

        /// <summary>
        /// Fetches details, cast, videos and posters in one request. The trailer is picked by
        /// language: the requested languages in order, then English, then the title's original
        /// language (e.g. a Japanese PV for an anime), which needs a second request since TMDB
        /// only returns videos in the languages asked for. The poster likewise follows
        /// posterLanguages, then English, then a textless poster.
        /// </summary>
        private async Task<MetadataResult?> FetchAndMapAsync(
            string kind, int id, IReadOnlyList<string> trailerLanguages, IReadOnlyList<string> posterLanguages,
            CancellationToken cancellationToken)
        {
            var videoLanguages = trailerLanguages.Append("en").Distinct().ToList();
            var imageLanguages = posterLanguages.Append("en").Distinct().ToList();
            var url = $"{BaseUrl}/{kind}/{id}?api_key={_apiKey}&language={_language}&append_to_response=credits,videos,images" +
                      $"&include_video_language={string.Join(",", videoLanguages)}" +
                      $"&include_image_language={string.Join(",", imageLanguages)},null";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB lookup failed ({Status}): {Url}", response.StatusCode, RedactApiKey(url));
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<TmdbDetailsDto>(json, JsonOptions);
            if (dto is null)
            {
                return null;
            }

            var trailer = PickTrailer(dto.Videos?.Results, videoLanguages);
            var originalLanguage = dto.OriginalLanguage?.Trim().ToLowerInvariant();
            if (trailer is null && !string.IsNullOrEmpty(originalLanguage) && !videoLanguages.Contains(originalLanguage))
            {
                var originalVideos = await FetchVideosAsync(kind, id, originalLanguage, cancellationToken).ConfigureAwait(false);
                trailer = PickTrailer(originalVideos, new[] { originalLanguage });
            }

            var posterPath = PickPoster(dto.Images?.Posters, imageLanguages) ?? dto.PosterPath;

            return new MetadataResult
            {
                Overview = dto.Overview,
                PosterUrl = string.IsNullOrEmpty(posterPath) ? null : ImageBaseUrl + posterPath,
                BackdropUrl = string.IsNullOrEmpty(dto.BackdropPath) ? null : BackdropImageBaseUrl + dto.BackdropPath,
                TrailerYoutubeKey = trailer?.Key,
                TrailerLanguage = trailer?.Language,
                Cast = dto.Credits?.Cast?
                    .OrderBy(c => c.Order)
                    .Take(10)
                    .Select(c => new CastMember
                    {
                        Name = c.Name ?? string.Empty,
                        Character = c.Character ?? string.Empty,
                        ProfileImageUrl = string.IsNullOrEmpty(c.ProfilePath) ? null : ImageBaseUrl + c.ProfilePath
                    })
                    .ToList() ?? new List<CastMember>()
            };
        }

        // First language with a YouTube video wins; within it a "Trailer" beats teasers/clips and
        // an official upload beats a fan upload.
        private static TmdbVideoDto? PickTrailer(IEnumerable<TmdbVideoDto>? videos, IEnumerable<string> languages)
        {
            var youtube = videos?
                .Where(v => string.Equals(v.Site, "YouTube", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(v.Key))
                .ToList();
            if (youtube is not { Count: > 0 })
            {
                return null;
            }

            foreach (var language in languages)
            {
                var match = youtube
                    .Where(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(v => string.Equals(v.Type, "Trailer", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(v => v.Official)
                    .FirstOrDefault();
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        // First language with a poster wins, then a textless one; within a language the
        // best-rated poster.
        private static string? PickPoster(IEnumerable<TmdbImageDto>? posters, IEnumerable<string> languages)
        {
            var candidates = posters?.Where(p => !string.IsNullOrEmpty(p.FilePath)).ToList();
            if (candidates is not { Count: > 0 })
            {
                return null;
            }

            foreach (var language in languages.Append(null))
            {
                var match = candidates
                    .Where(p => string.Equals(p.Language, language, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.VoteAverage)
                    .ThenByDescending(p => p.VoteCount)
                    .FirstOrDefault();
                if (match is not null)
                {
                    return match.FilePath;
                }
            }

            return null;
        }

        private async Task<List<TmdbVideoDto>?> FetchVideosAsync(
            string kind, int id, string language, CancellationToken cancellationToken)
        {
            var url = $"{BaseUrl}/{kind}/{id}/videos?api_key={_apiKey}&language={language}&include_video_language={language}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("TMDB video lookup failed ({Status}): {Url}", response.StatusCode, RedactApiKey(url));
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<TmdbVideosDto>(json, JsonOptions)?.Results;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "TMDB video lookup failed: {Url}", RedactApiKey(url));
                return null;
            }
        }

        private static string RedactApiKey(string url) =>
            System.Text.RegularExpressions.Regex.Replace(url, @"api_key=[^&]+", "api_key=***");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        // --- TMDB API DTOs (subset of fields we actually use) ---

        private class TmdbDetailsDto
        {
            public string? Overview { get; set; }

            [JsonPropertyName("poster_path")]
            public string? PosterPath { get; set; }

            [JsonPropertyName("backdrop_path")]
            public string? BackdropPath { get; set; }

            [JsonPropertyName("original_language")]
            public string? OriginalLanguage { get; set; }

            public TmdbCreditsDto? Credits { get; set; }
            public TmdbVideosDto? Videos { get; set; }
            public TmdbImagesDto? Images { get; set; }
        }

        private class TmdbImagesDto
        {
            public List<TmdbImageDto>? Posters { get; set; }
        }

        private class TmdbImageDto
        {
            [JsonPropertyName("file_path")]
            public string? FilePath { get; set; }

            [JsonPropertyName("iso_639_1")]
            public string? Language { get; set; }

            [JsonPropertyName("vote_average")]
            public double VoteAverage { get; set; }

            [JsonPropertyName("vote_count")]
            public int VoteCount { get; set; }
        }

        private class TmdbCreditsDto
        {
            public List<TmdbCastDto>? Cast { get; set; }
        }

        private class TmdbCastDto
        {
            public string? Name { get; set; }
            public string? Character { get; set; }

            [JsonPropertyName("profile_path")]
            public string? ProfilePath { get; set; }

            public int Order { get; set; }
        }

        private class TmdbVideosDto
        {
            public List<TmdbVideoDto>? Results { get; set; }
        }

        private class TmdbVideoDto
        {
            public string? Key { get; set; }
            public string? Site { get; set; }
            public string? Type { get; set; }
            public bool Official { get; set; }

            [JsonPropertyName("iso_639_1")]
            public string? Language { get; set; }
        }

        private class TmdbFindResponse
        {
            [JsonPropertyName("tv_results")]
            public List<TmdbTvResultDto>? TvResults { get; set; }
        }

        private class TmdbTvResultDto
        {
            public int Id { get; set; }
        }

        private class TmdbReleaseDatesResponse
        {
            public List<TmdbCountryReleaseDatesDto>? Results { get; set; }
        }

        private class TmdbCountryReleaseDatesDto
        {
            [JsonPropertyName("iso_3166_1")]
            public string? CountryCode { get; set; }

            [JsonPropertyName("release_dates")]
            public List<TmdbReleaseDateDto>? ReleaseDates { get; set; }
        }

        private class TmdbReleaseDateDto
        {
            [JsonPropertyName("release_date")]
            public DateTime? ReleaseDate { get; set; }

            public int Type { get; set; }
        }
    }
}
