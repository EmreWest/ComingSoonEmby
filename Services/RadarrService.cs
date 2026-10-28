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
    /// Pulls upcoming movies from Radarr. Since different configured lists may resolve a movie's
    /// "release date" from different fields (digital/physical/cinema), GetUpcomingRawAsync
    /// returns every movie with all three raw dates intact and lets each caller resolve/filter
    /// via ResolveDate for its own list's window. Radarr's /calendar endpoint matches on ANY of
    /// the three release-date fields, so we query a widened window (CalendarQueryPaddingDays) to
    /// avoid missing movies whose specific date of interest sits outside Radarr's own match.
    /// </summary>
    public class RadarrService
    {
        private const int CalendarQueryPaddingDays = 120;

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public RadarrService(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Pings Radarr's system/status endpoint to verify the configured URL + API key work,
        /// for the Dashboard's "Test Connection" button - does not touch the calendar.
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync(
            string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return (false, "Server URL is empty.");
            }

            try
            {
                var url = $"{baseUrl.TrimEnd('/')}/api/v3/system/status";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Api-Key", apiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode
                    ? (true, "Connected successfully.")
                    : (false, $"Radarr responded with {(int)response.StatusCode} {response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return (false, $"Could not reach Radarr: {ex.Message}");
            }
        }

        public async Task<List<CalendarItem>> GetUpcomingRawAsync(
            string baseUrl,
            string apiKey,
            int daysAhead,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow.Date;
            var start = now.AddDays(-1); // small back-buffer for timezone edge cases
            var end = now.AddDays(daysAhead + CalendarQueryPaddingDays);

            var url = $"{baseUrl.TrimEnd('/')}/api/v3/calendar" +
                      $"?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}&unmonitored=false";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var movies = JsonSerializer.Deserialize<List<RadarrMovieDto>>(json, JsonOptions) ?? new List<RadarrMovieDto>();

            var results = movies.Select(movie => new CalendarItem
            {
                Type = CalendarItemType.Movie,
                Title = movie.Title ?? string.Empty,
                Overview = movie.Overview ?? string.Empty,
                TmdbId = movie.TmdbId,
                InCinemasDate = movie.InCinemas,
                PhysicalReleaseDate = movie.PhysicalRelease,
                DigitalReleaseDate = movie.DigitalRelease,
                PosterUrl = movie.Images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl,
                TrailerYoutubeKey = string.IsNullOrWhiteSpace(movie.YouTubeTrailerId) ? null : movie.YouTubeTrailerId
            }).ToList();

            _logger.LogInformation("Radarr: {Count} movies fetched within the next {Days}+{Padding} days", results.Count, daysAhead, CalendarQueryPaddingDays);

            return results;
        }

        /// <summary>
        /// Collapses movies returned by several Radarr instances (e.g. a 1080p and a 4K server
        /// tracking the same title) into one item per TMDB id. The first instance wins; fields it
        /// left empty are filled from the others. Movies without a TMDB id are kept as-is.
        /// </summary>
        public static List<CalendarItem> MergeInstances(IEnumerable<CalendarItem> movies)
        {
            var results = new List<CalendarItem>();
            var byTmdbId = new Dictionary<int, CalendarItem>();

            foreach (var movie in movies)
            {
                if (!movie.TmdbId.HasValue)
                {
                    results.Add(movie);
                    continue;
                }

                if (!byTmdbId.TryGetValue(movie.TmdbId.Value, out var existing))
                {
                    byTmdbId[movie.TmdbId.Value] = movie;
                    results.Add(movie);
                    continue;
                }

                existing.InCinemasDate ??= movie.InCinemasDate;
                existing.PhysicalReleaseDate ??= movie.PhysicalReleaseDate;
                existing.DigitalReleaseDate ??= movie.DigitalReleaseDate;
                existing.PosterUrl ??= movie.PosterUrl;
                existing.TrailerYoutubeKey ??= movie.TrailerYoutubeKey;
                if (string.IsNullOrWhiteSpace(existing.Overview))
                {
                    existing.Overview = movie.Overview;
                }
            }

            return results;
        }

        /// <summary>Resolves a movie's "release date" for a specific list's configured date type.</summary>
        public static DateTime? ResolveDate(
            CalendarItem item, RadarrReleaseDateType dateType, RegionalReleaseDates? regionalDates = null)
        {
            // A non-null regional result means TMDB answered successfully for the requested
            // country. In that case stay strictly regional: a missing date must not silently be
            // replaced with Radarr's commonly US-focused value. Null means regional lookup was
            // unavailable (no key or a technical failure), where Radarr remains the safe fallback.
            if (regionalDates is not null)
            {
                return dateType switch
                {
                    RadarrReleaseDateType.Digital => regionalDates.Digital,
                    RadarrReleaseDateType.Physical => regionalDates.Physical,
                    RadarrReleaseDateType.InCinemas => regionalDates.InCinemas,
                    RadarrReleaseDateType.Earliest => EarliestOf(
                        regionalDates.InCinemas, regionalDates.Physical, regionalDates.Digital),
                    _ => regionalDates.Digital
                };
            }

            return dateType switch
            {
                RadarrReleaseDateType.Digital => item.DigitalReleaseDate,
                RadarrReleaseDateType.Physical => item.PhysicalReleaseDate,
                RadarrReleaseDateType.InCinemas => item.InCinemasDate,
                RadarrReleaseDateType.Earliest => EarliestOf(
                    item.InCinemasDate, item.PhysicalReleaseDate, item.DigitalReleaseDate),
                _ => item.DigitalReleaseDate
            };
        }

        private static DateTime? EarliestOf(params DateTime?[] dates)
        {
            var present = dates.Where(d => d.HasValue).Select(d => d!.Value).ToList();
            return present.Count == 0 ? null : present.Min();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // --- Radarr API DTOs (subset of fields we actually use) ---

        private class RadarrMovieDto
        {
            public string? Title { get; set; }
            public string? Overview { get; set; }

            [JsonPropertyName("tmdbId")]
            public int? TmdbId { get; set; }

            public DateTime? InCinemas { get; set; }
            public DateTime? PhysicalRelease { get; set; }
            public DateTime? DigitalRelease { get; set; }

            public string? YouTubeTrailerId { get; set; }

            public List<RadarrImageDto>? Images { get; set; }
        }

        private class RadarrImageDto
        {
            public string? CoverType { get; set; }
            public string? RemoteUrl { get; set; }
        }
    }
}
