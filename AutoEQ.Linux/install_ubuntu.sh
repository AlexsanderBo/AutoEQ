#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
APP="$SCRIPT_DIR/autoeq-linux"

if [[ ! -x "$APP" ]]; then
  echo "autoeq-linux executable not found or not executable: $APP"
  echo "Run: chmod +x \"$APP\""
  exit 1
fi

if ! command -v pactl >/dev/null 2>&1 || ! command -v parec >/dev/null 2>&1; then
  echo "Missing pactl/parec. Install Ubuntu audio utilities:"
  echo "sudo apt install pulseaudio-utils pipewire pipewire-pulse"
  exit 1
fi

"$APP" --install-pipewire --install-startup --setup-only

echo
echo "AutoEQ Linux files installed."
echo "Restart PipeWire and select 'AutoEQ Sink' in Ubuntu sound settings:"
echo "systemctl --user restart pipewire pipewire-pulse"
echo
echo "To run AutoEQ in the background now:"
echo "\"$APP\" --background"
