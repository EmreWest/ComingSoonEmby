using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ComingSoonPlugin.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace ComingSoonPlugin
{
    /// <summary>
    /// Main plugin entry point. Registered by Emby Server via MEF/assembly scan on startup.
    /// Exposes IHasWebPages so the Dashboard can show our config page under Plugins.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        public static Plugin? Instance { get; private set; }

        public override string Name => "Coming Soon";

        public override string Description =>
            "Shows a Coming Soon row on the home screen, sourced from Sonarr/Radarr calendars.";

        public override Guid Id => Guid.Parse("6f2a9e2b-3c1d-4b7e-9a6a-1f0d8c5e2b71");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Root folder for everything this plugin keeps on disk. Hides (not overrides)
        /// BasePlugin.DataFolderPath since that points at Emby's generic per-plugin storage.
        /// </summary>
        public new string DataFolderPath
        {
            get
            {
                var path = Path.Combine(ApplicationPaths.PluginsPath, "ComingSoon", "data");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// One real folder tree per configured list, each holding one subfolder per entry
        /// (poster.jpg, video, movie.nfo) that a normal Emby library is pointed at - see
        /// ComingSoonFileLibrarySync. Folder name is derived from the list's own Name; renaming a
        /// list in settings creates a fresh folder/library rather than renaming the old one.
        /// </summary>
        public string GetLibraryDataPath(string listName)
        {
            var folderName = "library-" + ComingSoonEntryPaths.SafeFileName(listName);
            var path = Path.Combine(DataFolderPath, folderName);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Emby's subtitle-fonts folder (Dashboard > Playback > Subtitles > Fonts).</summary>
        public string FontsPath => Path.Combine(ApplicationPaths.ProgramDataPath, "fonts");

        /// <summary>Emby's own log directory, read by the config page's log viewer.</summary>
        public string LogDirectoryPath => ApplicationPaths.LogDirectoryPath;

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream($"{type.Namespace}.thumb.png")!;
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var ns = GetType().Namespace;

            // EnableInMainMenu also lists the page in the Dashboard sidebar (server section), so it
            // can be opened without going through Plugins.
            yield return new PluginPageInfo
            {
                Name = "ComingSoon",
                DisplayName = "Coming Soon",
                EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", ns),
                IsMainConfigPage = true,
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "theaters"
            };

            yield return new PluginPageInfo
            {
                Name = "ComingSoonConfigJS",
                EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.js", ns)
            };
        }
    }
}
