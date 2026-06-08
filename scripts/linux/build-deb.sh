#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
VERSION="${1:-}"

if [[ -z "$VERSION" ]]; then
  VERSION="$(grep -m1 '<Version>' "$REPO_ROOT/Directory.Build.props" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/')"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required." >&2
  exit 1
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "dpkg-deb is required. On Ubuntu: sudo apt install dpkg-dev" >&2
  exit 1
fi

PUBLISH_DIR="$REPO_ROOT/build/linux/publish"
PKG_ROOT="$REPO_ROOT/build/linux/deb/autoeq-linux_${VERSION}_amd64"
DIST_DIR="$REPO_ROOT/dist/linux"

rm -rf "$PUBLISH_DIR" "$PKG_ROOT"
mkdir -p "$PUBLISH_DIR" "$PKG_ROOT/DEBIAN" "$PKG_ROOT/usr/bin" "$PKG_ROOT/usr/share/doc/autoeq-linux" "$PKG_ROOT/usr/share/autoeq-linux" "$PKG_ROOT/usr/share/applications" "$DIST_DIR"

dotnet publish "$REPO_ROOT/AutoEQ.Linux/AutoEQ.Linux.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  -o "$PUBLISH_DIR"

install -m 0755 "$PUBLISH_DIR/autoeq-linux" "$PKG_ROOT/usr/bin/autoeq-linux"
install -m 0755 "$REPO_ROOT/AutoEQ.Linux/install_ubuntu.sh" "$PKG_ROOT/usr/share/autoeq-linux/install_ubuntu.sh"
install -m 0644 "$REPO_ROOT/README.md" "$PKG_ROOT/usr/share/doc/autoeq-linux/README.md"
install -m 0644 "$REPO_ROOT/packaging/linux/autoeq-linux.desktop" "$PKG_ROOT/usr/share/applications/autoeq-linux.desktop"

sed "s/@VERSION@/$VERSION/g" "$REPO_ROOT/packaging/linux/control.template" > "$PKG_ROOT/DEBIAN/control"
install -m 0755 "$REPO_ROOT/packaging/linux/postinst" "$PKG_ROOT/DEBIAN/postinst"
install -m 0755 "$REPO_ROOT/packaging/linux/prerm" "$PKG_ROOT/DEBIAN/prerm"

dpkg-deb --build --root-owner-group "$PKG_ROOT" "$DIST_DIR/autoeq-linux_${VERSION}_amd64.deb"
echo "DEB output: $DIST_DIR/autoeq-linux_${VERSION}_amd64.deb"
