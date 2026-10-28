using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Downloads a poster and burns a "release date" pill into the bottom-right corner, plus an
    /// "UPCOMING"/"IN CINEMAS" pill (and season/episode pill for series) top-left. Writes to
    /// {outputDirectory}/poster.jpg - see ComingSoonEntryPaths for how that directory is chosen.
    /// Also downloads a backdrop (badged with "IN CINEMAS" when applicable) to
    /// {outputDirectory}/backdrop.jpg for the home-screen hero image. The pristine source image
    /// is cached in a hidden file and only ever downloaded once, but the badge overlay is
    /// re-rendered from that cache on every call, so date/season/language/list changes show up
    /// on the next refresh without any extra network traffic. The cache is replaced when the
    /// source changes (another language, artwork from the user's own library, or that library
    /// image itself being changed).
    /// </summary>
    public class ImageOverlayService
    {
        // Must match the LogicalName set on the EmbeddedResource item in ComingSoonPlugin.csproj.
        private const string FontResourceName = "ComingSoonPlugin.Fonts.Roboto-Bold.ttf";

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        // Emby's subtitle-fonts folder (Dashboard > Playback > Subtitles > Fonts) - reused here
        // if present, falling back to the bundled font otherwise.
        private readonly string? _embyFontsDirectory;
        private readonly FontCollection _fonts = new();
        private readonly ComingSoonLanguage _language;
        private readonly BadgeCorner _badgePosition;

        private FontFamily? _resolvedFontFamily;
        private bool _fontResolutionAttempted;

        public ImageOverlayService(
            HttpClient httpClient, ILogger logger, string? embyFontsDirectory = null,
            ComingSoonLanguage language = ComingSoonLanguage.English,
            BadgeCorner badgePosition = BadgeCorner.TopLeft)
        {
            _httpClient = httpClient;
            _logger = logger;
            _embyFontsDirectory = embyFontsDirectory;
            _language = language;
            _badgePosition = badgePosition;
        }

        /// <summary>
        /// Renders poster.jpg from posterSource (an http(s) URL or a local file, e.g. an Emby
        /// library image). Returns true if a new source image was fetched, so Emby must re-read
        /// the entry's images.
        /// </summary>
        public async Task<bool> DownloadAndBadgeAsync(
            string posterSource,
            DateTime releaseDate,
            string outputDirectory,
            CancellationToken cancellationToken,
            int? seasonNumber = null,
            int? episodeNumber = null,
            ReleaseBadgeKind badgeKind = ReleaseBadgeKind.Upcoming,
            string? dateRegionLabel = null)
        {
            Directory.CreateDirectory(outputDirectory);
            var sourcePath = Path.Combine(outputDirectory, ".poster-source.jpg");
            var outputPath = Path.Combine(outputDirectory, "poster.jpg");

            var (available, changed) = await EnsureSourceAsync(posterSource, sourcePath, cancellationToken).ConfigureAwait(false);
            if (!available)
            {
                return false;
            }

            try
            {
                using var image = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken).ConfigureAwait(false);

                var font = LoadFont(image);
                if (font is not null)
                {
                    // The date badge sits in the bottom-right corner. If the status stack shares
                    // that corner, it starts above the date instead of overlapping it.
                    var dateText = FormatDate(releaseDate);
                    if (!string.IsNullOrWhiteSpace(dateRegionLabel))
                    {
                        dateText += $" · {dateRegionLabel.Trim().ToUpperInvariant()}";
                    }
                    var dateHeight = DrawPill(image, font, dateText, BadgeCorner.BottomRight, 0f, DateBadgeBackground);
                    var stackOffset = _badgePosition == BadgeCorner.BottomRight ? dateHeight + Gap(image) : 0f;

                    var statusHeight = DrawPill(image, font, StatusText(badgeKind), _badgePosition, stackOffset, UpcomingGreen);

                    if (seasonNumber.HasValue && episodeNumber.HasValue)
                    {
                        DrawPill(image, font, $"S{seasonNumber:D2}E{episodeNumber:D2}",
                            _badgePosition, stackOffset + statusHeight + Gap(image), UpcomingGreen);
                    }
                }

                await image.SaveAsJpegAsync(outputPath, cancellationToken).ConfigureAwait(false);
                return changed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to badge poster from {Path}", sourcePath);
                return changed;
            }
        }

        /// <summary>
        /// Downloads a backdrop/fanart image to {outputDirectory}/backdrop.jpg for the home-screen
        /// hero image. A release-specific badge (IN CINEMAS / ON DISC / DIGITAL) is drawn on it;
        /// the generic "Upcoming" kind is left off so plain rows keep a clean hero image.
        /// </summary>
        public async Task<bool> DownloadBackdropAsync(
            string backdropSource, string outputDirectory, CancellationToken cancellationToken,
            ReleaseBadgeKind badgeKind = ReleaseBadgeKind.Upcoming)
        {
            Directory.CreateDirectory(outputDirectory);
            var sourcePath = Path.Combine(outputDirectory, ".backdrop-source.jpg");
            var outputPath = Path.Combine(outputDirectory, "backdrop.jpg");

            var (available, changed) = await EnsureSourceAsync(backdropSource, sourcePath, cancellationToken).ConfigureAwait(false);
            if (!available)
            {
                return false;
            }

            if (badgeKind == ReleaseBadgeKind.Upcoming)
            {
                if (changed || !File.Exists(outputPath))
                {
                    File.Copy(sourcePath, outputPath, overwrite: true);
                }

                return changed;
            }

            try
            {
                using var image = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken).ConfigureAwait(false);

                var font = LoadFont(image);
                if (font is not null)
                {
                    DrawPill(image, font, StatusText(badgeKind), _badgePosition, 0f, UpcomingGreen);
                }

                await image.SaveAsJpegAsync(outputPath, cancellationToken).ConfigureAwait(false);
                return changed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to badge backdrop from {Path}", sourcePath);
                return changed;
            }
        }

        private string StatusText(ReleaseBadgeKind kind)
        {
            var german = _language == ComingSoonLanguage.German;
            return kind switch
            {
                ReleaseBadgeKind.InCinemas => german ? "IM KINO" : "IN CINEMAS",
                ReleaseBadgeKind.OnDisc => german ? "AUF DISC" : "ON DISC",
                ReleaseBadgeKind.Digital => "DIGITAL",
                _ => german ? "DEMNÄCHST" : "UPCOMING"
            };
        }

        private string FormatDate(DateTime date)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(
                _language == ComingSoonLanguage.German ? "de-DE" : "en-US");
            return date.ToString(_language == ComingSoonLanguage.German ? "d. MMM" : "MMM d", culture);
        }

        /// <summary>
        /// Makes sure sourcePath holds the image from source. The identity of what was fetched
        /// (URL, or local path plus modification time) is kept next to it, so the cached copy is
        /// replaced when the source changes. If fetching fails, an existing copy is kept.
        /// </summary>
        private async Task<(bool Available, bool Changed)> EnsureSourceAsync(
            string source, string sourcePath, CancellationToken cancellationToken)
        {
            var isLocal = !source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            var identity = isLocal ? $"{source}|{File.GetLastWriteTimeUtc(source).Ticks}" : source;
            var identityPath = sourcePath + ".from";

            if (File.Exists(sourcePath) && File.Exists(identityPath) && File.ReadAllText(identityPath) == identity)
            {
                return (true, false);
            }

            var tempPath = sourcePath + ".download";
            bool fetched;
            if (isLocal)
            {
                try
                {
                    File.Copy(source, tempPath, overwrite: true);
                    fetched = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to copy image from {Path}", source);
                    fetched = false;
                }
            }
            else
            {
                fetched = await DownloadToFileAsync(source, tempPath, cancellationToken).ConfigureAwait(false);
            }

            if (!fetched)
            {
                TryDelete(tempPath);
                return (File.Exists(sourcePath), false);
            }

            File.Move(tempPath, sourcePath, overwrite: true);
            File.WriteAllText(identityPath, identity);
            return (true, true);
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not delete {Path}", path);
            }
        }

        private async Task<bool> DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(destinationPath);
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image from {Url}", url);
                return false;
            }
        }

        private Font? LoadFont(Image image)
        {
            var family = ResolveFontFamily();
            if (family is null)
            {
                return null;
            }

            try
            {
                return family.Value.CreateFont(image.Height * 0.042f, FontStyle.Bold);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create badge font instance, skipping badge overlay");
                return null;
            }
        }

        private FontFamily? ResolveFontFamily()
        {
            if (_fontResolutionAttempted)
            {
                return _resolvedFontFamily;
            }

            _fontResolutionAttempted = true;

            var embyFontPath = FindFirstEmbyFont();
            if (embyFontPath is not null)
            {
                try
                {
                    _resolvedFontFamily = _fonts.Add(embyFontPath);
                    _logger.LogInformation("Coming Soon: using Emby subtitle font for poster badges: {Path}", embyFontPath);
                    return _resolvedFontFamily;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Emby subtitle font {Path}, falling back to bundled font", embyFontPath);
                }
            }

            try
            {
                using var fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FontResourceName)
                    ?? throw new InvalidOperationException(
                        $"Embedded font resource '{FontResourceName}' not found - did you add a font file at " +
                        "Fonts/Roboto-Bold.ttf before building? See Fonts/README.txt.");

                _resolvedFontFamily = _fonts.Add(fontStream);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load embedded fallback font either - badge overlays will be skipped entirely");
                _resolvedFontFamily = null;
            }

            return _resolvedFontFamily;
        }

        private string? FindFirstEmbyFont()
        {
            if (string.IsNullOrWhiteSpace(_embyFontsDirectory) || !Directory.Exists(_embyFontsDirectory))
            {
                return null;
            }

            return Directory.EnumerateFiles(_embyFontsDirectory)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static readonly Color UpcomingGreen = Color.FromRgb(82, 181, 75);
        private static readonly Color DateBadgeBackground = Color.FromRgba(0, 0, 0, 180);

        private static float Margin(Image image) => image.Width * 0.04f;
        private static float Gap(Image image) => image.Height * 0.012f;

        /// <summary>
        /// Draws a rounded white-text pill in the given corner, offset by <paramref name="stackOffset"/>
        /// pixels away from that corner (so a caller can stack pills: top corners grow downward,
        /// bottom corners grow upward). Returns the pill's height for the next stack offset.
        /// </summary>
        private static float DrawPill(
            Image<Rgba32> image, Font font, string text, BadgeCorner corner, float stackOffset, Color background)
        {
            var ink = TextMeasurer.MeasureSize(text, new RichTextOptions(font) { Origin = PointF.Empty });

            var paddingX = image.Width * 0.022f;
            var paddingY = image.Height * 0.013f;
            var pillWidth = ink.Width + (paddingX * 2);
            var pillHeight = ink.Height + (paddingY * 2);
            var margin = Margin(image);

            var isRight = corner is BadgeCorner.TopRight or BadgeCorner.BottomRight;
            var isBottom = corner is BadgeCorner.BottomLeft or BadgeCorner.BottomRight;

            var x = isRight ? image.Width - margin - pillWidth : margin;
            var y = isBottom
                ? image.Height - margin - pillHeight - stackOffset
                : margin + stackOffset;

            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(x + (pillWidth / 2f), y + (pillHeight / 2f)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            image.Mutate(ctx =>
            {
                FillPill(ctx, x, y, pillWidth, pillHeight, background);
                ctx.DrawText(textOptions, text, Color.White);
            });

            return pillHeight;
        }

        /// <summary>
        /// Fills a stadium/pill shape: a center rectangle plus two end-cap circles of the same
        /// color - SixLabors.ImageSharp.Drawing 2.1.x has no rounded-rectangle primitive.
        /// </summary>
        private static void FillPill(IImageProcessingContext ctx, float x, float y, float width, float height, Color color)
        {
            var radius = height / 2f;
            var brush = new SolidBrush(color);
            var centerWidth = Math.Max(0, width - (radius * 2));

            ctx.Fill(brush, new SixLabors.ImageSharp.Drawing.RectangularPolygon(x + radius, y, centerWidth, height));
            ctx.Fill(brush, new SixLabors.ImageSharp.Drawing.EllipsePolygon(x + radius, y + radius, radius));
            ctx.Fill(brush, new SixLabors.ImageSharp.Drawing.EllipsePolygon(x + width - radius, y + radius, radius));
        }
    }
}
