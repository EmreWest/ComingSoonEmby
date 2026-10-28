#!/bin/sh
# Copies the plugin into Emby's plugins folder, mounted at $PLUGIN_DIR. Optional PUID/PGID set
# the file's owner to match the user Emby runs as.
set -eu

if [ ! -d "$PLUGIN_DIR" ]; then
    echo "Coming Soon: $PLUGIN_DIR does not exist - mount Emby's plugins folder there." >&2
    exit 1
fi

cp /plugin/ComingSoonPlugin.dll "$PLUGIN_DIR/ComingSoonPlugin.dll"

if [ -n "${PUID:-}" ]; then
    chown "$PUID:${PGID:-$PUID}" "$PLUGIN_DIR/ComingSoonPlugin.dll"
fi

echo "Coming Soon: plugin installed to $PLUGIN_DIR - (re)start Emby to load it."
