using System;
using System.Collections.Generic;

namespace ComingSoonPlugin.Services
{
    /// <summary>
    /// Maps the language names Radarr and Sonarr use ("German", "Portuguese (Brazil)", ...) to
    /// ISO 639-1 codes for TMDB. A fixed table rather than CultureInfo, since Emby can run with
    /// .NET's invariant globalization mode, where culture names aren't available.
    /// </summary>
    public static class ArrLanguages
    {
        private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["English"] = "en", ["French"] = "fr", ["Spanish"] = "es", ["Spanish (Latino)"] = "es",
            ["German"] = "de", ["Italian"] = "it", ["Danish"] = "da", ["Dutch"] = "nl", ["Flemish"] = "nl",
            ["Japanese"] = "ja", ["Icelandic"] = "is", ["Chinese"] = "zh", ["Russian"] = "ru",
            ["Polish"] = "pl", ["Vietnamese"] = "vi", ["Swedish"] = "sv", ["Norwegian"] = "no",
            ["Finnish"] = "fi", ["Turkish"] = "tr", ["Portuguese"] = "pt", ["Portuguese (Brazil)"] = "pt",
            ["Greek"] = "el", ["Korean"] = "ko", ["Hungarian"] = "hu", ["Hebrew"] = "he",
            ["Lithuanian"] = "lt", ["Czech"] = "cs", ["Hindi"] = "hi", ["Romanian"] = "ro", ["Thai"] = "th",
            ["Bulgarian"] = "bg", ["Arabic"] = "ar", ["Ukrainian"] = "uk", ["Persian"] = "fa",
            ["Bengali"] = "bn", ["Slovak"] = "sk", ["Latvian"] = "lv", ["Catalan"] = "ca",
            ["Croatian"] = "hr", ["Serbian"] = "sr", ["Bosnian"] = "bs", ["Estonian"] = "et",
            ["Tamil"] = "ta", ["Indonesian"] = "id", ["Telugu"] = "te", ["Macedonian"] = "mk",
            ["Slovenian"] = "sl", ["Malayalam"] = "ml", ["Kannada"] = "kn", ["Albanian"] = "sq",
            ["Afrikaans"] = "af", ["Marathi"] = "mr", ["Tagalog"] = "tl", ["Urdu"] = "ur",
            ["Romansh"] = "rm", ["Mongolian"] = "mn", ["Georgian"] = "ka", ["Malay"] = "ms"
        };

        /// <summary>
        /// Returns the ISO 639-1 code for a Radarr/Sonarr language name. "Original" resolves to
        /// originalLanguage; "Any", "Unknown" and unmapped names return null (no preference).
        /// </summary>
        public static string? ToIsoCode(string? name, string? originalLanguage = null)
        {
            if (string.Equals(name, "Original", StringComparison.OrdinalIgnoreCase))
            {
                return ToIsoCode(originalLanguage);
            }

            return name is not null && Codes.TryGetValue(name.Trim(), out var code) ? code : null;
        }
    }
}
