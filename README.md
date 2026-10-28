# Coming Soon for Emby

> **About this fork:** This is a fork of [mrt187/ComingSoonEmby](https://github.com/mrt187/ComingSoonEmby).
> It adds support for several Sonarr/Radarr servers, realtime library monitoring, trailers that play
> in place of the placeholder video, and a ready-made Docker image for installing the plugin with
> docker compose. See [What's new in this fork](#whats-new-in-this-fork). For the original plugin,
> issues and upstream releases, use the original repository.

Coming Soon creates one or more Emby home-screen rows containing upcoming movies and TV episodes
from one or more Radarr and Sonarr servers.

## What's new in this fork

- **Multiple Sonarr and Radarr servers.** Add as many servers as you like, e.g. a 1080p and a 4K
  Radarr. Their calendars are combined, and a title tracked by several servers appears only once.
  If one server is unreachable, existing entries of that type are kept. Existing single-server
  settings are carried over automatically.
- **Realtime monitoring.** The Coming Soon libraries are created with Emby's realtime monitor
  turned on, so new, changed and expired entries show up right after a refresh instead of waiting
  for the next library scan. Existing libraries are switched over on the next refresh.
- **Trailers play instead of the placeholder.** Trailers are downloaded from YouTube and, by
  default, become the entry's video, so pressing Play starts the trailer. This can be switched back
  to Emby's separate "Trailer" button under **TMDB & Trailers**.
- **Trailers without a TMDB key.** Movie trailers fall back to the YouTube trailer Radarr knows
  about. TV show trailers still need a TMDB key.
- **Docker image.** `ghcr.io/emrewest/comingsoonemby` installs the plugin from docker compose, see
  [Installation with docker compose](#installation-with-docker-compose).

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

1. Download `ComingSoonPlugin.dll` from the latest
   [release of this fork](https://github.com/EmreWest/ComingSoonEmby/releases), or use docker
   compose as described below.
2. Copy it into the Emby Server `plugins` folder.
3. Restart Emby Server.
4. Open **Dashboard > Plugins > Coming Soon** and add your Sonarr/Radarr servers.
5. Run **Refresh Coming Soon** once under **Scheduled Tasks**.
6. Add the created library or libraries to the desired users' home screens.

## Installation with docker compose

The image `ghcr.io/emrewest/comingsoonemby` contains only the plugin. It copies
`ComingSoonPlugin.dll` into Emby's plugins folder and exits, so Emby starts with the plugin
installed. Mount the same plugins folder into both containers:

```yaml
services:
  comingsoon-plugin:
    image: ghcr.io/emrewest/comingsoonemby:latest
    volumes:
      # Official emby/embyserver image: <config>/plugins
      # linuxserver/emby image:         <config>/data/plugins
      - ./emby/config/plugins:/plugins
    restart: "no"

  emby:
    image: emby/embyserver:latest
    volumes:
      - ./emby/config:/config
    depends_on:
      comingsoon-plugin:
        condition: service_completed_successfully
        restart: true
    # ...ports, media volumes etc. as before
```

To update, run `docker compose pull && docker compose up -d`. When the plugin image changed,
compose reruns the installer and restarts Emby. Set `PUID`/`PGID` on the installer if the copied
file should belong to the user Emby runs as.

Tags: `latest` follows `main`, `1.3.0` / `1.3` are published for release tags, and `sha-<commit>`
for every build.

## Where files are stored

The generated libraries are stored below:

```text
{Emby plugins folder}/ComingSoon/data/library-<List Name>/
```

Each entry contains its poster, backdrop, NFO metadata and its video: the downloaded trailer, or a
short placeholder when no trailer is available. With "Play the trailer instead of the placeholder
video" turned off, the trailer is stored in the entry's `trailers` folder instead.

<sub>Developed with the assistance of AI.</sub>
