# Coming Soon for Emby

Coming Soon creates one or more Emby home-screen rows containing upcoming movies and TV episodes
from one or more Radarr and Sonarr servers.

## Supported Emby versions

- Emby Server 4.9.5 Stable
- Emby Server 4.10 Beta

Version 1.3.0 is built against Emby 4.9.5 and tested on Emby 4.10 Beta.

## How it works

1. The scheduled task reads upcoming movies and episodes from every enabled Radarr and Sonarr
   server. A title tracked by several servers (e.g. a 1080p and a 4K Radarr) appears only once.
2. For movies, TMDB supplies cinema, digital or physical release dates for the country selected
   in each list. Germany is the default. If that date is missing, a verified US date is used and
   visibly marked as `US`; Radarr is only used when regional lookup is unavailable.
3. Poster, description and release information are collected and a release badge is added.
4. Every configured list becomes a normal Emby library with realtime monitoring turned on, so
   changes show up right after a refresh, and can be placed on the home screen.
5. Expired entries are removed after the next successful refresh. If a Sonarr or Radarr server is
   briefly unavailable, existing entries of that type are kept.

Trailers are downloaded from YouTube with yt-dlp (provisioned automatically if it isn't installed)
and, by default, become the entry's video, so pressing Play starts the trailer. Entries without a
trailer keep a short placeholder video.

A TMDB API key enables regional movie dates, verified US fallback dates and TV show trailers.
Without a TMDB key, the basic Coming Soon libraries still work, movie dates come from Radarr and
movie trailers use the YouTube trailer Radarr knows about.

## Installation

1. Download `ComingSoonPlugin.dll` from the latest GitHub release.
2. Copy it into the Emby Server `plugins` folder.
3. Restart Emby Server.
4. Open **Dashboard > Plugins > Coming Soon** and add your Sonarr/Radarr servers.
5. Run **Refresh Coming Soon** once under **Scheduled Tasks**.
6. Add the created library or libraries to the desired users' home screens.

## Where files are stored

The generated libraries are stored below:

```text
{Emby plugins folder}/ComingSoon/data/library-<List Name>/
```

Each entry contains its poster, backdrop, NFO metadata and its video: the downloaded trailer, or a
short placeholder when no trailer is available. With "Play the trailer instead of the placeholder
video" turned off, the trailer is stored in the entry's `trailers` folder instead.

<sub>Developed with the assistance of AI.</sub>
