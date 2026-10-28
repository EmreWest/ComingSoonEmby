using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComingSoonPlugin.Models;
using Microsoft.Extensions.Logging;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Pulls upcoming episodes from Sonarr's calendar. Unlike Radarr, Sonarr episodes have a
    /// single unambiguous airDateUtc, so there's no "which date type" question here.
    /// </summary>
    public class SonarrService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public SonarrService(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Pings Sonarr's system/status endpoint to verify the configured URL + API key work,
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
                    : (false, $"Sonarr responded with {(int)response.StatusCode} {response.StatusCode}.");
            }
            catch (Exception ex)
            {
                return (false, $"Could not reach Sonarr: {ex.Message}");
            }
        }

        public async Task<List<CalendarItem>> GetUpcomingAsync(
            string baseUrl,
            string apiKey,
            int daysAhead,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var start = now.Date;
            var end = start.AddDays(daysAhead);

            var url = $"{baseUrl.TrimEnd('/')}/api/v3/calendar" +
                      $"?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}" +
                      "&includeSeries=true&unmonitored=false";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var episodes = JsonSerializer.Deserialize<List<SonarrEpisodeDto>>(json, JsonOptions) ?? new List<SonarrEpisodeDto>();
            var profileLanguages = episodes.Count > 0
                ? await GetProfileLanguagesAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false)
                : new Dictionary<int, string?>();

            var results = episodes
                // Sonarr's calendar query works with whole dates and therefore also returns
                // episodes that aired earlier today. Compare the full UTC timestamp so an
                // episode disappears as soon as its actual release time has passed.
                .Where(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value > now)
                .Select(e => new CalendarItem
                {
                    Type = CalendarItemType.Episode,
                    Title = e.Series?.Title ?? e.SeriesTitle ?? "Unknown Series",
                    Date = e.AirDateUtc!.Value,
                    Overview = e.Overview ?? e.Series?.Overview ?? string.Empty,
                    TvdbId = e.Series?.TvdbId,
                    SeasonNumber = e.SeasonNumber,
                    EpisodeNumber = e.EpisodeNumber,
                    EpisodeTitle = e.Title,
                    PosterUrl = e.Series?.Images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl,
                    ArtworkLanguage = ArrLanguages.ToIsoCode(
                        profileLanguages.GetValueOrDefault(e.Series?.LanguageProfileId ?? 0), e.Series?.OriginalLanguage?.Name)
                })
                .OrderBy(r => r.Date)
                .ToList();

            _logger.LogInformation("Sonarr: {Count} upcoming episodes within next {Days} days", results.Count, daysAhead);

            return results;
        }

        /// <summary>
        /// Cutoff language of each language profile. Only Sonarr v3 has language profiles; v4
        /// replaced them with custom formats and only keeps a placeholder for API compatibility,
        /// so v4 yields nothing and posters fall back to the plugin language.
        /// </summary>
        private async Task<Dictionary<int, string?>> GetProfileLanguagesAsync(
            string baseUrl, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                using (var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/v3/system/status"))
                {
                    statusRequest.Headers.Add("X-Api-Key", apiKey);
                    using var statusResponse = await _httpClient.SendAsync(statusRequest, cancellationToken).ConfigureAwait(false);
                    var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var version = JsonSerializer.Deserialize<SonarrStatusDto>(statusJson, JsonOptions)?.Version;
                    if (!statusResponse.IsSuccessStatusCode || version is null || !version.StartsWith("3.", StringComparison.Ordinal))
                    {
                        return new Dictionary<int, string?>();
                    }
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/v3/languageprofile");
                request.Headers.Add("X-Api-Key", apiKey);
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new Dictionary<int, string?>();
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (JsonSerializer.Deserialize<List<SonarrLanguageProfileDto>>(json, JsonOptions) ?? new List<SonarrLanguageProfileDto>())
                    .Where(p => !string.Equals(p.Name, "Deprecated", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(p => p.Id, p => p.Cutoff?.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Sonarr: could not read language profiles");
                return new Dictionary<int, string?>();
            }
        }

        /// <summary>
        /// Collapses episodes returned by several Sonarr instances into one item per series and
        /// season/episode number, keeping the first instance's copy. Episodes without a TVDB id
        /// can't be matched and are kept as-is.
        /// </summary>
        public static List<CalendarItem> MergeInstances(IEnumerable<CalendarItem> episodes)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return episodes
                .Where(e => !e.TvdbId.HasValue || seen.Add(e.StableKey))
                .OrderBy(e => e.Date)
                .ToList();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // --- Sonarr API DTOs (subset of fields we actually use) ---

        private class SonarrEpisodeDto
        {
            public string? Title { get; set; }
            public string? Overview { get; set; }
            public int? SeasonNumber { get; set; }
            public int? EpisodeNumber { get; set; }
            public DateTime? AirDateUtc { get; set; }

            /// <summary>Fallback if the nested Series object isn't present for some reason.</summary>
            public string? SeriesTitle { get; set; }

            public SonarrSeriesDto? Series { get; set; }
        }

        private class SonarrSeriesDto
        {
            public string? Title { get; set; }
            public string? Overview { get; set; }
            public int? TvdbId { get; set; }
            public int? LanguageProfileId { get; set; }
            public SonarrLanguageDto? OriginalLanguage { get; set; }
            public List<SonarrImageDto>? Images { get; set; }
        }

        private class SonarrStatusDto
        {
            public string? Version { get; set; }
        }

        private class SonarrLanguageProfileDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public SonarrLanguageDto? Cutoff { get; set; }
        }

        private class SonarrLanguageDto
        {
            public string? Name { get; set; }
        }

        private class SonarrImageDto
        {
            public string? CoverType { get; set; }
            public string? RemoteUrl { get; set; }
        }
    }
}
