# Installer image for the Coming Soon plugin. It holds only ComingSoonPlugin.dll and copies it
# into Emby's plugins folder on start, then exits - run it before Emby in docker compose (see
# README). Works with any Emby image, since the plugin itself is architecture-independent.

ARG EMBY_VERSION=4.9.5.0

# The Emby SDK assemblies the plugin compiles against. 4.9.5 isn't published on NuGet, so they
# are taken from the matching official server image.
FROM --platform=$BUILDPLATFORM emby/embyserver:${EMBY_VERSION} AS emby-sdk

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
COPY --from=emby-sdk /system/MediaBrowser.Common.dll /system/MediaBrowser.Controller.dll /system/MediaBrowser.Model.dll lib/
RUN dotnet build ComingSoonPlugin.csproj -c Release

# `docker build --target dll --output type=local,dest=out .` exports just the DLL.
FROM scratch AS dll
COPY --from=build /src/bin/Release/net8.0/ComingSoonPlugin.dll /

FROM alpine:3.20
COPY --from=build /src/bin/Release/net8.0/ComingSoonPlugin.dll /plugin/ComingSoonPlugin.dll
COPY docker/install.sh /install.sh
ENV PLUGIN_DIR=/plugins
ENTRYPOINT ["/bin/sh", "/install.sh"]
