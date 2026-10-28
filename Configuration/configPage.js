define(["emby-input", "emby-button", "emby-checkbox", "emby-select"], function () {
    "use strict";

    var pluginId = "6f2a9e2b-3c1d-4b7e-9a6a-1f0d8c5e2b71";

    // Release notes are loaded from the server (/ComingSoon/Changelog, backed by the embedded
    // changelog.json) rather than hardcoded here, so they stay current even when the browser
    // still has an older cached copy of this file. Edit changelog.json to update them.
    var changelogCache = null;

    var text = {
        English: {
            pageDescription: "Pulls upcoming releases from Sonarr/Radarr and shows them as one or more \"Coming Soon\" rows.",
            infoButton: "ℹ️ Info",
            logsButton: "📋 Logs",
            readmeButton: "📖 Readme",
            readmeHeading: "📖 How it works",
            readmeHowHeading: "What the plugin does",
            readmeHowText: "The scheduled task reads upcoming movies from one or more Radarr servers and episodes from one or more Sonarr servers. For movies, TMDB supplies release dates for the country selected in each list. It creates one normal Emby library for every configured list with realtime monitoring turned on, so Emby picks up changes as soon as a refresh writes them.",
            readmeStorageHeading: "Where files are stored",
            readmeStorageText: "Each entry is stored inside the plugin data folder with its poster, backdrop and NFO file. Its video is the downloaded trailer when one is available, otherwise a short placeholder. Removing an expired entry removes its folder on the next successful refresh.",
            readmeVersionsHeading: "Supported Emby versions",
            readmeVersionsText: "Emby Server 4.9.5 Stable and Emby Server 4.10 Beta. Built against 4.9.5 and tested on 4.10 Beta.",
            daysAheadLabel: "Days ahead to show",
            daysAheadDescription: "How far into the future to pull upcoming releases from the Sonarr/Radarr calendars (up to 365 days).",
            sonarrSummary: "Sonarr",
            radarrSummary: "Radarr",
            sonarrDescription: "Add one or more Sonarr servers. Upcoming episodes from all enabled servers are combined; an episode tracked by several servers appears only once.",
            radarrDescription: "Add one or more Radarr servers (e.g. a 1080p and a 4K instance). Upcoming movies from all enabled servers are combined; a movie tracked by several servers appears only once.",
            addSonarrButton: "+ Add Sonarr server",
            addRadarrButton: "+ Add Radarr server",
            instanceNameLabel: "Name",
            enabledLabel: "Enabled",
            serverUrlLabel: "Server URL",
            sonarrUrlDescription: "Base URL of your Sonarr server, e.g. http://localhost:8989.",
            radarrUrlDescription: "Base URL of your Radarr server, e.g. http://localhost:7878.",
            apiKeyLabel: "API Key",
            sonarrApiKeyDescription: "Found in Sonarr under Settings > General > Security > API Key.",
            radarrApiKeyDescription: "Found in Radarr under Settings > General > Security > API Key.",
            testConnection: "Test Connection",
            listsSummary: "Lists",
            listsDescription: "Each list becomes its own Emby library, shown as its own row on the home screen. A title can appear in more than one list.",
            addListButton: "+ Add List",
            listNameLabel: "List name",
            includeMoviesLabel: "Include movies",
            includeTvLabel: "Include TV shows",
            movieDateLabel: "Movie date",
            movieDateOptions: { Digital: "Digital Release", Physical: "Physical Release", InCinemas: "In Cinemas", Earliest: "Earliest of all three" },
            releaseRegionLabel: "Movie release region",
            releaseRegionDescription: "Uses TMDB dates for this country. If the selected regional date is missing, a verified US date is used and marked as US. Radarr is only used when regional lookup is unavailable.",
            releaseRegionOptions: { DE: "Germany", AT: "Austria", CH: "Switzerland", US: "United States", GB: "United Kingdom" },
            removeButton: "Remove",
            tmdbSummary: "TMDB & Trailers",
            tmdbApiKeyLabel: "API Key (optional - needed for regional dates and local trailers)",
            tmdbApiKeyDescription: "Enables country-specific movie dates, verified US fallback dates and local trailer downloads. Without it, movie dates fall back to Radarr. Free key at themoviedb.org/settings/api.",
            downloadTrailersLabel: "Download trailers locally (recommended for Apple TV / native clients)",
            downloadTrailersDescription: "Downloads the trailer from YouTube as a real file so it plays inside Emby instead of opening YouTube. Movie trailers come from TMDB or, without a TMDB key, from Radarr; TV show trailers need a TMDB key.",
            trailerAsMainVideoLabel: "Play the trailer instead of the placeholder video",
            trailerAsMainVideoDescription: "Pressing Play on an entry starts its trailer. Entries without a trailer keep the short placeholder. When off, the trailer is available through Emby's \"Trailer\" button instead.",
            ytDlpPathLabel: "yt-dlp path",
            ytDlpDescription: "Default \"yt-dlp\" tries PATH first, then auto-downloads a standalone copy into the plugin's own data folder if not found - works regardless of which Docker image is used.",
            ffmpegPathLabel: "ffmpeg path (optional)",
            ffmpegDescription: "Leave empty to download a ready-to-play trailer (up to 720p) with no ffmpeg needed. Set a path to a modern ffmpeg only if you want higher-resolution merged trailers.",
            languageSummary: "Appearance",
            languageSelectLabel: "Badge text & description language",
            languageDescription: "Affects poster badge text, the release-date caption, and fetched overview text.",
            badgePositionLabel: "Badge position",
            badgePositionDescription: "Which corner the status badge sits in. The release date always stays bottom-right; if the badge shares that corner it sits above the date.",
            badgePosOptions: { TopLeft: "Top left", TopRight: "Top right", BottomLeft: "Bottom left", BottomRight: "Bottom right" },
            saveButton: "Save",
            testingText: "Testing…",
            unknownResult: "Unknown result",
            saveFailedPrefix: "Save failed: ",
            infoHeading: "ℹ️ Coming Soon",
            releaseNotesHeading: "Release Notes",
            tmdbHeading: "TMDB API Key",
            tmdbInfo: "Optional. Used for country-specific movie release dates, local trailers via yt-dlp " +
                "(so playback stays inside Emby instead of opening YouTube), and as a fallback for the overview/poster if " +
                "Emby's own metadata provider isn't configured. Cast/crew is normally fetched by Emby " +
                "itself once an item is scanned.",
            close: "Close",
            logHeading: "📋 Coming Soon Logs",
            logDescription: "Recent log lines for this plugin, newest at the bottom.",
            refresh: "Refresh",
            openInNewTab: "🗗 Open in new tab",
            loadingLogs: "Loading…",
            noLogs: "No log lines yet - run a refresh first.",
            logsFailedPrefix: "Failed to load logs: "
        },
        German: {
            pageDescription: "Holt kommende Veröffentlichungen von Sonarr/Radarr und zeigt sie als eine oder mehrere \"Coming Soon\"-Zeilen an.",
            infoButton: "ℹ️ Info",
            logsButton: "📋 Logs",
            readmeButton: "📖 Readme",
            readmeHeading: "📖 So funktioniert es",
            readmeHowHeading: "Was das Plugin macht",
            readmeHowText: "Die geplante Aufgabe liest kommende Filme aus einem oder mehreren Radarr-Servern und Episoden aus einem oder mehreren Sonarr-Servern. Für Filme liefert TMDB die Veröffentlichungstermine des in der jeweiligen Liste gewählten Landes. Für jede eingerichtete Liste wird eine normale Emby-Bibliothek mit aktivierter Echtzeitüberwachung erstellt, damit Emby Änderungen sofort nach einer Aktualisierung übernimmt.",
            readmeStorageHeading: "Wo die Dateien gespeichert werden",
            readmeStorageText: "Jeder Eintrag liegt im Datenordner des Plugins – mit Poster, Hintergrundbild und NFO-Datei. Als Video dient der heruntergeladene Trailer, sofern vorhanden, sonst ein kurzes Platzhaltervideo. Abgelaufene Einträge werden nach der nächsten erfolgreichen Aktualisierung entfernt.",
            readmeVersionsHeading: "Unterstützte Emby-Versionen",
            readmeVersionsText: "Emby Server 4.9.5 Stable und Emby Server 4.10 Beta. Gebaut gegen 4.9.5 und getestet auf 4.10 Beta.",
            daysAheadLabel: "Tage im Voraus anzeigen",
            daysAheadDescription: "Wie weit in die Zukunft kommende Veröffentlichungen aus den Sonarr/Radarr-Kalendern geholt werden (bis zu 365 Tage).",
            sonarrSummary: "Sonarr",
            radarrSummary: "Radarr",
            sonarrDescription: "Einen oder mehrere Sonarr-Server hinzufügen. Kommende Episoden aller aktivierten Server werden zusammengeführt; eine Episode, die mehrere Server verfolgen, erscheint nur einmal.",
            radarrDescription: "Einen oder mehrere Radarr-Server hinzufügen (z.B. eine 1080p- und eine 4K-Instanz). Kommende Filme aller aktivierten Server werden zusammengeführt; ein Film, den mehrere Server verfolgen, erscheint nur einmal.",
            addSonarrButton: "+ Sonarr-Server hinzufügen",
            addRadarrButton: "+ Radarr-Server hinzufügen",
            instanceNameLabel: "Name",
            enabledLabel: "Aktiviert",
            serverUrlLabel: "Server-URL",
            sonarrUrlDescription: "Basis-URL deines Sonarr-Servers, z.B. http://localhost:8989.",
            radarrUrlDescription: "Basis-URL deines Radarr-Servers, z.B. http://localhost:7878.",
            apiKeyLabel: "API-Schlüssel",
            sonarrApiKeyDescription: "Zu finden in Sonarr unter Einstellungen > Allgemein > Sicherheit > API-Schlüssel.",
            radarrApiKeyDescription: "Zu finden in Radarr unter Einstellungen > Allgemein > Sicherheit > API-Schlüssel.",
            testConnection: "Verbindung testen",
            listsSummary: "Listen",
            listsDescription: "Jede Liste wird zu einer eigenen Emby-Bibliothek und erscheint als eigene Zeile auf der Startseite. Ein Titel kann in mehreren Listen vorkommen.",
            addListButton: "+ Liste hinzufügen",
            listNameLabel: "Listenname",
            includeMoviesLabel: "Filme einschließen",
            includeTvLabel: "Serien einschließen",
            movieDateLabel: "Filmdatum",
            movieDateOptions: { Digital: "Digitale Veröffentlichung", Physical: "Physische Veröffentlichung", InCinemas: "Kinostart", Earliest: "Frühestes aller drei" },
            releaseRegionLabel: "Veröffentlichungsregion für Filme",
            releaseRegionDescription: "Verwendet TMDB-Termine für dieses Land. Fehlt der ausgewählte regionale Termin, wird ein bestätigter US-Termin verwendet und mit US markiert. Radarr wird nur verwendet, wenn die regionale Abfrage nicht verfügbar ist.",
            releaseRegionOptions: { DE: "Deutschland", AT: "Österreich", CH: "Schweiz", US: "USA", GB: "Vereinigtes Königreich" },
            removeButton: "Entfernen",
            tmdbSummary: "TMDB & Trailer",
            tmdbApiKeyLabel: "API-Schlüssel (optional - für regionale Termine und lokale Trailer)",
            tmdbApiKeyDescription: "Aktiviert länderspezifische Filmtermine, bestätigte US-Ersatztermine und lokale Trailer-Downloads. Ohne Schlüssel werden Radarr-Termine verwendet. Kostenloser Schlüssel unter themoviedb.org/settings/api.",
            downloadTrailersLabel: "Trailer lokal herunterladen (empfohlen für Apple TV / native Clients)",
            downloadTrailersDescription: "Lädt den Trailer von YouTube als echte Datei herunter, damit er in Emby abgespielt wird statt YouTube zu öffnen. Film-Trailer kommen von TMDB oder, ohne TMDB-Schlüssel, von Radarr; Serien-Trailer benötigen einen TMDB-Schlüssel.",
            trailerAsMainVideoLabel: "Trailer statt Platzhaltervideo abspielen",
            trailerAsMainVideoDescription: "Beim Abspielen eines Eintrags startet dessen Trailer. Einträge ohne Trailer behalten das kurze Platzhaltervideo. Ist die Option aus, ist der Trailer stattdessen über Embys \"Trailer\"-Schaltfläche erreichbar.",
            ytDlpPathLabel: "yt-dlp-Pfad",
            ytDlpDescription: "Standard \"yt-dlp\" versucht zuerst PATH, lädt bei Bedarf automatisch eine eigene Kopie in den Plugin-Datenordner herunter - funktioniert unabhängig vom verwendeten Docker-Image.",
            ffmpegPathLabel: "ffmpeg-Pfad (optional)",
            ffmpegDescription: "Leer lassen, um einen fertig abspielbaren Trailer (bis 720p) ohne ffmpeg herunterzuladen. Pfad zu einem modernen ffmpeg nur setzen, wenn höher aufgelöste, zusammengeführte Trailer gewünscht sind.",
            languageSummary: "Darstellung",
            languageSelectLabel: "Sprache für Badge-Text & Beschreibung",
            languageDescription: "Betrifft den Poster-Badge-Text, die Datumsanzeige und die abgerufene Beschreibung.",
            badgePositionLabel: "Badge-Position",
            badgePositionDescription: "In welcher Ecke der Status-Badge sitzt. Das Datum bleibt immer unten rechts; teilt sich der Badge diese Ecke, sitzt er über dem Datum.",
            badgePosOptions: { TopLeft: "Oben links", TopRight: "Oben rechts", BottomLeft: "Unten links", BottomRight: "Unten rechts" },
            saveButton: "Speichern",
            testingText: "Teste…",
            unknownResult: "Unbekanntes Ergebnis",
            saveFailedPrefix: "Speichern fehlgeschlagen: ",
            infoHeading: "ℹ️ Coming Soon",
            releaseNotesHeading: "Versionshinweise",
            tmdbHeading: "TMDB API-Schlüssel",
            tmdbInfo: "Optional. Wird für länderspezifische Filmtermine, lokale Trailer über yt-dlp " +
                "(damit die Wiedergabe in Emby bleibt statt YouTube zu öffnen) sowie als Fallback für " +
                "Beschreibung/Poster, falls Emby's eigener Metadaten-Provider nicht konfiguriert ist. " +
                "Cast/Crew werden normalerweise von Emby selbst beim Scannen geladen.",
            close: "Schließen",
            logHeading: "📋 Coming Soon Logs",
            logDescription: "Letzte Log-Zeilen dieses Plugins, neueste unten.",
            refresh: "Aktualisieren",
            openInNewTab: "🗗 In neuem Tab öffnen",
            loadingLogs: "Lädt…",
            noLogs: "Noch keine Log-Einträge - erst einen Refresh ausführen.",
            logsFailedPrefix: "Logs konnten nicht geladen werden: "
        }
    };

    return function (view) {
        var currentLanguage = "English";
        var currentLists = [];
        var currentInstances = { Sonarr: [], Radarr: [] };

        function t() {
            return text[currentLanguage] || text.English;
        }

        // --- Static text (everything outside the dynamic list rows) ---

        function applyStaticText() {
            var s = t();

            view.querySelector('#CsPageDescription').textContent = s.pageDescription;
            view.querySelector('#BtnInfo').textContent = s.infoButton;
            view.querySelector('#BtnLogs').textContent = s.logsButton;
            view.querySelector('#BtnReadme').textContent = s.readmeButton;

            view.querySelector('#DaysAhead').setAttribute('label', s.daysAheadLabel);
            view.querySelector('#CsDaysAheadDescription').textContent = s.daysAheadDescription;

            view.querySelector('#CsSonarrDescription').textContent = s.sonarrDescription;
            view.querySelector('#CsRadarrDescription').textContent = s.radarrDescription;
            view.querySelector('#BtnAddSonarr').textContent = s.addSonarrButton;
            view.querySelector('#BtnAddRadarr').textContent = s.addRadarrButton;

            view.querySelector('#CsListsSummary').textContent = s.listsSummary;
            view.querySelector('#CsListsDescription').textContent = s.listsDescription;
            view.querySelector('#BtnAddList').textContent = s.addListButton;

            view.querySelector('#CsTmdbSummary').textContent = s.tmdbSummary;
            view.querySelector('#TmdbApiKey').setAttribute('label', s.tmdbApiKeyLabel);
            view.querySelector('#CsTmdbApiKeyDescription').textContent = s.tmdbApiKeyDescription;
            view.querySelector('#CsDownloadTrailersLabel').textContent = s.downloadTrailersLabel;
            view.querySelector('#CsDownloadTrailersDescription').textContent = s.downloadTrailersDescription;
            view.querySelector('#CsTrailerAsMainVideoLabel').textContent = s.trailerAsMainVideoLabel;
            view.querySelector('#CsTrailerAsMainVideoDescription').textContent = s.trailerAsMainVideoDescription;
            view.querySelector('#YtDlpPath').setAttribute('label', s.ytDlpPathLabel);
            view.querySelector('#CsYtDlpDescription').textContent = s.ytDlpDescription;
            view.querySelector('#FfmpegPath').setAttribute('label', s.ffmpegPathLabel);
            view.querySelector('#CsFfmpegDescription').textContent = s.ffmpegDescription;

            view.querySelector('#CsLanguageSummary').textContent = s.languageSummary;
            view.querySelector('#Language').setAttribute('label', s.languageSelectLabel);
            view.querySelector('#CsLanguageDescription').textContent = s.languageDescription;

            var badgeSel = view.querySelector('#BadgePosition');
            badgeSel.setAttribute('label', s.badgePositionLabel);
            Array.prototype.forEach.call(badgeSel.options, function (opt) {
                opt.textContent = s.badgePosOptions[opt.value] || opt.value;
            });
            view.querySelector('#CsBadgePositionDescription').textContent = s.badgePositionDescription;

            view.querySelector('#CsSaveButtonLabel').textContent = s.saveButton;

            view.querySelector('#CsInfoHeading').textContent = s.infoHeading;
            view.querySelector('#BtnCloseInfo').textContent = s.close;

            view.querySelector('#CsReadmeHeading').textContent = s.readmeHeading;
            view.querySelector('#CsReadmeHowHeading').textContent = s.readmeHowHeading;
            view.querySelector('#CsReadmeHowText').textContent = s.readmeHowText;
            view.querySelector('#CsReadmeStorageHeading').textContent = s.readmeStorageHeading;
            view.querySelector('#CsReadmeStorageText').textContent = s.readmeStorageText;
            view.querySelector('#CsReadmeVersionsHeading').textContent = s.readmeVersionsHeading;
            view.querySelector('#CsReadmeVersionsText').textContent = s.readmeVersionsText;
            view.querySelector('#BtnCloseReadme').textContent = s.close;

            view.querySelector('#CsLogHeading').textContent = s.logHeading;
            view.querySelector('#CsLogDescription').textContent = s.logDescription;
            view.querySelector('#BtnRefreshLogs').textContent = s.refresh;
            view.querySelector('#BtnOpenLogsNewTab').textContent = s.openInNewTab;
            view.querySelector('#BtnCloseLogs').textContent = s.close;
        }

        // --- Info modal ---

        function openReadmeModal() {
            view.querySelector('#CsReadmeModalBackdrop').classList.add('open');
        }

        function closeReadmeModal() {
            view.querySelector('#CsReadmeModalBackdrop').classList.remove('open');
        }

        function renderChangelog(entries) {
            var container = view.querySelector('#CsReleaseNotes');
            container.innerHTML = '';
            // Only the newest version's notes are shown.
            entries.slice(0, 1).forEach(function (entry) {
                var block = document.createElement('div');
                block.className = 'csVersionBlock';

                var versionLine = document.createElement('div');
                versionLine.className = 'csVersionNumber';
                versionLine.textContent = entry.Version || entry.version;
                block.appendChild(versionLine);

                var list = document.createElement('ul');
                (entry.Notes || entry.notes || []).forEach(function (note) {
                    var li = document.createElement('li');
                    li.textContent = note;
                    list.appendChild(li);
                });
                block.appendChild(list);

                container.appendChild(block);
            });
        }

        function renderInfoModal() {
            var s = t();
            view.querySelector('#CsReleaseNotesHeading').textContent = s.releaseNotesHeading;
            view.querySelector('#CsTmdbInfoHeading').textContent = s.tmdbHeading;
            view.querySelector('#CsTmdbInfoText').textContent = s.tmdbInfo;

            if (changelogCache) {
                renderChangelog(changelogCache);
                return;
            }

            ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('ComingSoon/Changelog'),
                dataType: 'json'
            }).then(function (entries) {
                changelogCache = entries || [];
                renderChangelog(changelogCache);
            }).catch(function () {
                view.querySelector('#CsReleaseNotes').textContent = '';
            });
        }

        function openInfoModal() {
            renderInfoModal();
            view.querySelector('#CsInfoModalBackdrop').classList.add('open');
        }

        function closeInfoModal() {
            view.querySelector('#CsInfoModalBackdrop').classList.remove('open');
        }

        // --- Log modal ---

        function loadLogs() {
            var s = t();
            var box = view.querySelector('#CsLogBox');
            box.textContent = s.loadingLogs;

            ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('ComingSoon/Logs', { MaxLines: 300 }),
                dataType: 'json'
            }).then(function (result) {
                var s2 = t();
                box.textContent = (result && result.Text) ? result.Text : s2.noLogs;
                box.scrollTop = box.scrollHeight;
            }).catch(function (err) {
                describeError(err).then(function (msg) {
                    box.textContent = t().logsFailedPrefix + msg;
                });
            });
        }

        function openLogModal() {
            var s = t();
            view.querySelector('#CsLogHeading').textContent = s.logHeading;
            view.querySelector('#CsLogDescription').textContent = s.logDescription;
            view.querySelector('#BtnRefreshLogs').textContent = s.refresh;
            view.querySelector('#BtnOpenLogsNewTab').textContent = s.openInNewTab;
            view.querySelector('#BtnCloseLogs').textContent = s.close;
            view.querySelector('#CsLogModalBackdrop').classList.add('open');
            loadLogs();
        }

        function closeLogModal() {
            view.querySelector('#CsLogModalBackdrop').classList.remove('open');
        }

        function escapeHtml(str) {
            return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        // Renders the current log text into a standalone page in a new tab (via a Blob URL, no
        // server round-trip) so it's easier to read/search full-screen than the modal's fixed-
        // height box.
        function openLogsInNewTab() {
            var text = view.querySelector('#CsLogBox').textContent;
            var html = '<pre style="white-space:pre-wrap;word-break:break-word;' +
                'font-family:monospace;font-size:14px;line-height:1.4;background:#000;color:#ddd;' +
                'padding:1em;margin:0;">' + escapeHtml(text) + '</pre>';
            var blob = new Blob([html], { type: 'text/html' });
            var url = URL.createObjectURL(blob);
            window.open(url, '_blank');
        }

        // --- Dynamic Sonarr/Radarr server editor ---

        var defaultPorts = { Sonarr: 8989, Radarr: 7878 };

        function defaultInstance(kind) {
            var count = currentInstances[kind].length;
            return {
                Name: count ? kind + ' ' + (count + 1) : kind,
                Enabled: true,
                Url: 'http://localhost:' + defaultPorts[kind],
                ApiKey: ''
            };
        }

        // Before 1.3 each kind had exactly one server stored in flat fields. Show it as the first
        // instance so it's carried over into the instance list on the next Save.
        function instancesFromConfig(config, kind) {
            var saved = config[kind + 'Instances'];
            if (saved && saved.length) {
                return saved.map(function (i) {
                    return { Name: i.Name || '', Enabled: i.Enabled !== false, Url: i.Url || '', ApiKey: i.ApiKey || '' };
                });
            }

            if (config[kind + 'Enabled'] || config[kind + 'ApiKey']) {
                return [{ Name: kind, Enabled: !!config[kind + 'Enabled'], Url: config[kind + 'Url'] || '', ApiKey: config[kind + 'ApiKey'] || '' }];
            }

            return [];
        }

        function buildTextInput(type, label, value, onInput, description) {
            var wrap = document.createElement('div');
            wrap.className = 'inputContainer';
            var input = document.createElement('input', { is: 'emby-input' });
            input.type = type;
            input.setAttribute('label', label);
            input.value = value;
            input.addEventListener('input', function () { onInput(input.value); });
            wrap.appendChild(input);
            if (description) {
                var desc = document.createElement('div');
                desc.className = 'fieldDescription';
                desc.textContent = description;
                wrap.appendChild(desc);
            }
            return wrap;
        }

        function renderInstances(kind) {
            var s = t();
            var instances = currentInstances[kind];
            var container = view.querySelector('#' + kind + 'InstancesContainer');
            container.innerHTML = '';

            var summary = s[kind.toLowerCase() + 'Summary'];
            view.querySelector('#Cs' + kind + 'Summary').textContent =
                instances.length ? summary + ' (' + instances.length + ')' : summary;

            instances.forEach(function (instance, index) {
                var row = document.createElement('div');
                row.className = 'csListRow';

                row.appendChild(buildTextInput('text', s.instanceNameLabel, instance.Name,
                    function (v) { instance.Name = v; }));

                var enabledLabel = document.createElement('label');
                enabledLabel.className = 'emby-checkbox-label';
                var enabledCheckbox = document.createElement('input', { is: 'emby-checkbox' });
                enabledCheckbox.type = 'checkbox';
                enabledCheckbox.checked = instance.Enabled;
                enabledCheckbox.addEventListener('change', function () { instance.Enabled = enabledCheckbox.checked; });
                var enabledSpan = document.createElement('span');
                enabledSpan.textContent = s.enabledLabel;
                enabledLabel.appendChild(enabledCheckbox);
                enabledLabel.appendChild(enabledSpan);
                row.appendChild(enabledLabel);

                row.appendChild(buildTextInput('text', s.serverUrlLabel, instance.Url,
                    function (v) { instance.Url = v; }, s[kind.toLowerCase() + 'UrlDescription']));
                row.appendChild(buildTextInput('password', s.apiKeyLabel, instance.ApiKey,
                    function (v) { instance.ApiKey = v; }, s[kind.toLowerCase() + 'ApiKeyDescription']));

                var testResult = document.createElement('div');
                testResult.className = 'fieldDescription';
                testResult.style.display = 'none';
                testResult.style.marginTop = '0.5em';

                var buttons = document.createElement('div');
                buttons.className = 'csHeaderButtons';
                buttons.style.marginTop = '0.5em';

                var testBtn = document.createElement('button', { is: 'emby-button' });
                testBtn.type = 'button';
                testBtn.className = 'raised';
                testBtn.textContent = s.testConnection;
                testBtn.addEventListener('click', function () { testConnection(kind, instance, testResult); });
                buttons.appendChild(testBtn);

                var removeBtn = document.createElement('button', { is: 'emby-button' });
                removeBtn.type = 'button';
                removeBtn.className = 'raised';
                removeBtn.textContent = s.removeButton;
                removeBtn.addEventListener('click', function () {
                    instances.splice(index, 1);
                    renderInstances(kind);
                });
                buttons.appendChild(removeBtn);

                row.appendChild(buttons);
                row.appendChild(testResult);
                container.appendChild(row);
            });
        }

        function addInstance(kind) {
            currentInstances[kind].push(defaultInstance(kind));
            renderInstances(kind);
        }

        // --- Dynamic list editor ---
        // Mirrors the createElement(tag, {is: ...}) pattern used elsewhere for Emby's custom
        // elements (emby-checkbox/emby-select) rather than innerHTML strings, so dynamically
        // inserted rows get upgraded the same way as elements present at initial page load.

        function defaultList() {
            return { Name: "Coming Soon", IncludeMovies: true, MovieDateType: "Digital", ReleaseDateRegion: "DE", IncludeTvShows: true };
        }

        function buildDateTypeSelect(value) {
            var s = t();
            var select = document.createElement("select", { is: "emby-select" });
            select.setAttribute("label", s.movieDateLabel);
            ["Digital", "Physical", "InCinemas", "Earliest"].forEach(function (key) {
                var opt = document.createElement("option");
                opt.value = key;
                opt.textContent = s.movieDateOptions[key];
                select.appendChild(opt);
            });
            select.value = value;
            return select;
        }

        function buildReleaseRegionSelect(value) {
            var s = t();
            var select = document.createElement("select", { is: "emby-select" });
            select.setAttribute("label", s.releaseRegionLabel);
            ["DE", "AT", "CH", "US", "GB"].forEach(function (key) {
                var opt = document.createElement("option");
                opt.value = key;
                opt.textContent = s.releaseRegionOptions[key];
                select.appendChild(opt);
            });
            select.value = value || "DE";
            return select;
        }

        function renderLists() {
            var s = t();
            var container = view.querySelector('#ListsContainer');
            container.innerHTML = '';

            currentLists.forEach(function (list, index) {
                var row = document.createElement('div');
                row.className = 'csListRow';

                var nameWrap = document.createElement('div');
                nameWrap.className = 'inputContainer';
                var nameInput = document.createElement('input', { is: 'emby-input' });
                nameInput.type = 'text';
                nameInput.setAttribute('label', s.listNameLabel);
                nameInput.value = list.Name;
                nameInput.addEventListener('input', function () { list.Name = nameInput.value; });
                nameWrap.appendChild(nameInput);
                row.appendChild(nameWrap);

                var moviesLabel = document.createElement('label');
                moviesLabel.className = 'emby-checkbox-label';
                var moviesCheckbox = document.createElement('input', { is: 'emby-checkbox' });
                moviesCheckbox.type = 'checkbox';
                moviesCheckbox.checked = list.IncludeMovies;
                var moviesSpan = document.createElement('span');
                moviesSpan.textContent = s.includeMoviesLabel;
                moviesLabel.appendChild(moviesCheckbox);
                moviesLabel.appendChild(moviesSpan);
                row.appendChild(moviesLabel);

                var dateWrap = document.createElement('div');
                dateWrap.className = 'selectContainer';
                dateWrap.style.display = list.IncludeMovies ? '' : 'none';
                var dateSelect = buildDateTypeSelect(list.MovieDateType);
                dateSelect.addEventListener('change', function () { list.MovieDateType = dateSelect.value; });
                dateWrap.appendChild(dateSelect);
                row.appendChild(dateWrap);

                var regionWrap = document.createElement('div');
                regionWrap.className = 'selectContainer';
                regionWrap.style.display = list.IncludeMovies ? '' : 'none';
                var regionSelect = buildReleaseRegionSelect(list.ReleaseDateRegion);
                regionSelect.addEventListener('change', function () { list.ReleaseDateRegion = regionSelect.value; });
                regionWrap.appendChild(regionSelect);
                var regionDescription = document.createElement('div');
                regionDescription.className = 'fieldDescription';
                regionDescription.textContent = s.releaseRegionDescription;
                regionWrap.appendChild(regionDescription);
                row.appendChild(regionWrap);

                moviesCheckbox.addEventListener('change', function () {
                    list.IncludeMovies = moviesCheckbox.checked;
                    dateWrap.style.display = list.IncludeMovies ? '' : 'none';
                    regionWrap.style.display = list.IncludeMovies ? '' : 'none';
                });

                var tvLabel = document.createElement('label');
                tvLabel.className = 'emby-checkbox-label';
                var tvCheckbox = document.createElement('input', { is: 'emby-checkbox' });
                tvCheckbox.type = 'checkbox';
                tvCheckbox.checked = list.IncludeTvShows;
                tvCheckbox.addEventListener('change', function () { list.IncludeTvShows = tvCheckbox.checked; });
                var tvSpan = document.createElement('span');
                tvSpan.textContent = s.includeTvLabel;
                tvLabel.appendChild(tvCheckbox);
                tvLabel.appendChild(tvSpan);
                row.appendChild(tvLabel);

                var removeBtn = document.createElement('button', { is: 'emby-button' });
                removeBtn.type = 'button';
                removeBtn.className = 'raised';
                removeBtn.style.marginTop = '0.5em';
                removeBtn.textContent = s.removeButton;
                removeBtn.disabled = currentLists.length <= 1;
                removeBtn.addEventListener('click', function () {
                    currentLists.splice(index, 1);
                    renderLists();
                });
                row.appendChild(removeBtn);

                container.appendChild(row);
            });
        }

        function addList() {
            currentLists.push(defaultList());
            renderLists();
        }

        // --- Load/save ---

        function loadConfig() {
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                currentLanguage = config.Language || 'English';
                view.querySelector('#Language').value = currentLanguage;
                view.querySelector('#BadgePosition').value = config.BadgePosition || 'TopLeft';
                applyStaticText();

                currentInstances.Sonarr = instancesFromConfig(config, 'Sonarr');
                currentInstances.Radarr = instancesFromConfig(config, 'Radarr');
                renderInstances('Sonarr');
                renderInstances('Radarr');

                view.querySelector('#TmdbApiKey').value = config.TmdbApiKey || '';

                view.querySelector('#DaysAhead').value = config.DaysAhead;
                view.querySelector('#DownloadTrailersLocally').checked = config.DownloadTrailersLocally;
                view.querySelector('#TrailerAsMainVideo').checked = config.TrailerAsMainVideo !== false;
                view.querySelector('#YtDlpPath').value = config.YtDlpPath || 'yt-dlp';
                view.querySelector('#FfmpegPath').value = config.FfmpegPath || '';

                // Deduplicate by name so any duplicates left by the old config-growth bug are
                // cleaned up here and saved away on the next Save.
                var seenNames = {};
                currentLists = (config.Lists && config.Lists.length ? config.Lists : [defaultList()]).map(function (l) {
                    return {
                        Name: l.Name || 'Coming Soon',
                        IncludeMovies: l.IncludeMovies !== false,
                        MovieDateType: l.MovieDateType || 'Digital',
                        ReleaseDateRegion: l.ReleaseDateRegion || 'DE',
                        IncludeTvShows: l.IncludeTvShows !== false
                    };
                }).filter(function (l) {
                    var key = l.Name.trim().toLowerCase();
                    if (seenNames[key]) return false;
                    seenNames[key] = true;
                    return true;
                });
                renderLists();
            });
        }

        function buildConfigFromForm(config) {
            ['Sonarr', 'Radarr'].forEach(function (kind) {
                config[kind + 'Instances'] = currentInstances[kind].map(function (i) {
                    return { Name: (i.Name || '').trim(), Enabled: i.Enabled, Url: (i.Url || '').trim(), ApiKey: (i.ApiKey || '').trim() };
                });
                // The instance list is authoritative from now on; switching the old single-server
                // fields off keeps a deliberately emptied list from falling back to them.
                config[kind + 'Enabled'] = false;
            });

            config.TmdbApiKey = view.querySelector('#TmdbApiKey').value;
            config.Language = view.querySelector('#Language').value;
            config.BadgePosition = view.querySelector('#BadgePosition').value;

            config.DaysAhead = parseInt(view.querySelector('#DaysAhead').value, 10);
            config.DownloadTrailersLocally = view.querySelector('#DownloadTrailersLocally').checked;
            config.TrailerAsMainVideo = view.querySelector('#TrailerAsMainVideo').checked;
            config.YtDlpPath = view.querySelector('#YtDlpPath').value;
            config.FfmpegPath = view.querySelector('#FfmpegPath').value;

            config.Lists = currentLists.map(function (l) {
                return {
                    Name: (l.Name || 'Coming Soon').trim() || 'Coming Soon',
                    IncludeMovies: l.IncludeMovies,
                    MovieDateType: l.MovieDateType,
                    ReleaseDateRegion: l.ReleaseDateRegion || 'DE',
                    IncludeTvShows: l.IncludeTvShows
                };
            });
            return config;
        }

        // Emby's ApiClient rejects with a fetch Response object on HTTP errors; that
        // stringifies to "[object Response]", so extract a readable message instead.
        function describeError(err) {
            if (err && typeof err.text === "function" && typeof err.status !== "undefined") {
                var fallback = err.status + " " + (err.statusText || "Request failed");
                return err.text().then(function (body) {
                    var msg = body;
                    try {
                        var json = JSON.parse(body);
                        msg = (json.ResponseStatus && json.ResponseStatus.Message) || json.Message || body;
                    } catch (e) { /* body is not JSON */ }
                    return (msg && msg.trim()) || fallback;
                }).catch(function () {
                    return fallback;
                });
            }
            return Promise.resolve(err && err.message ? err.message : String(err));
        }

        function saveConfig(e) {
            e.preventDefault();
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                return ApiClient.updatePluginConfiguration(pluginId, buildConfigFromForm(config));
            }).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            }).catch(function (err) {
                describeError(err).then(function (msg) {
                    Dashboard.alert(t().saveFailedPrefix + msg);
                });
            });
        }

        function testConnection(kind, instance, resultEl) {
            var s = t();
            resultEl.style.display = 'block';
            resultEl.textContent = s.testingText;

            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('ComingSoon/Test' + kind),
                data: JSON.stringify({ Url: (instance.Url || '').trim(), ApiKey: (instance.ApiKey || '').trim() }),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function (result) {
                resultEl.textContent = (result && result.Success ? '✅ ' : '❌ ') + (result && result.Message ? result.Message : t().unknownResult);
            }).catch(function (err) {
                describeError(err).then(function (msg) {
                    resultEl.textContent = '❌ ' + msg;
                });
            });
        }

        // Wire up the handlers immediately. The controller runs once the view's DOM is
        // ready, so we must NOT defer this to a "viewshow" event - Emby does not reliably
        // dispatch viewshow to plugin config views, and when it doesn't fire the Save
        // button would otherwise never get a click handler.
        view.querySelector('#ComingSoonConfigForm').addEventListener('submit', saveConfig);
        view.querySelector('#BtnAddSonarr').addEventListener('click', function () { addInstance('Sonarr'); });
        view.querySelector('#BtnAddRadarr').addEventListener('click', function () { addInstance('Radarr'); });
        view.querySelector('#BtnAddList').addEventListener('click', addList);
        view.querySelector('#BtnInfo').addEventListener('click', openInfoModal);
        view.querySelector('#BtnReadme').addEventListener('click', openReadmeModal);
        view.querySelector('#BtnCloseReadme').addEventListener('click', closeReadmeModal);
        view.querySelector('#CsReadmeModalBackdrop').addEventListener('click', function (e) {
            if (e.target === e.currentTarget) closeReadmeModal();
        });
        view.querySelector('#BtnCloseInfo').addEventListener('click', closeInfoModal);
        view.querySelector('#CsInfoModalBackdrop').addEventListener('click', function (e) {
            if (e.target === e.currentTarget) closeInfoModal();
        });
        view.querySelector('#BtnLogs').addEventListener('click', openLogModal);
        view.querySelector('#BtnRefreshLogs').addEventListener('click', loadLogs);
        view.querySelector('#BtnOpenLogsNewTab').addEventListener('click', openLogsInNewTab);
        view.querySelector('#BtnCloseLogs').addEventListener('click', closeLogModal);
        view.querySelector('#CsLogModalBackdrop').addEventListener('click', function (e) {
            if (e.target === e.currentTarget) closeLogModal();
        });
        view.querySelector('#Language').addEventListener('change', function () {
            currentLanguage = view.querySelector('#Language').value;
            applyStaticText();
            renderInstances('Sonarr');
            renderInstances('Radarr');
            renderLists();
        });

        loadConfig();
        view.addEventListener('viewshow', loadConfig);
    };
});
