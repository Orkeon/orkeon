#!/usr/bin/env bash
# Builds the Debian package orkeon-archive-keyring_<ver>_all.deb: the public certificate that
# signs the Orkeon apt repository, as the binary keyring apt reads,
# /usr/share/keyrings/orkeon-archive-keyring.gpg.
#
# Usage:
#   scripts/package-keyring-deb.sh [--out artifacts/installers] [--keep-work]
#
# Inputs: installers/apt/orkeon-archive-keyring.asc (the armored certificate, the only form
# kept in git) and installers/apt/keyring.version (the package version, a date YYYY.MM.DD,
# changed with every change of the certificate). The build is reproducible: the same two
# inputs give the same bytes, whatever the clock or the umask, so a version is built once
# and its published bytes are reused afterwards, never rebuilt.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APT_DIR="$REPO_ROOT/installers/apt"
OUT="$REPO_ROOT/artifacts/installers"
KEEP_WORK=false

PACKAGE="orkeon-archive-keyring"
MAINTAINER="Orkeon Contributors <arion@orkeon.org>"
HOMEPAGE="https://github.com/Orkeon"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out)       OUT="$2"; shift 2 ;;
    --keep-work) KEEP_WORK=true; shift ;;
    -h|--help) sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v dpkg-deb >/dev/null 2>&1 || { echo "dpkg-deb not found (install the 'dpkg' package)." >&2; exit 1; }
command -v gpg >/dev/null 2>&1 || { echo "gpg not found (install the 'gnupg' package)." >&2; exit 1; }

CERT="$APT_DIR/orkeon-archive-keyring.asc"
[[ -f "$CERT" ]] || { echo "Missing $CERT" >&2; exit 1; }
VERSION="$(tr -d '[:space:]' < "$APT_DIR/keyring.version")"
[[ "$VERSION" =~ ^[0-9]{4}\.[0-9]{2}\.[0-9]{2}$ ]] || { echo "keyring.version must be a date YYYY.MM.DD, got '$VERSION'" >&2; exit 1; }

# The version is the date: it is also the timestamp of every file, so the build does not
# depend on when it runs. dpkg-deb reads SOURCE_DATE_EPOCH for the ar member times.
SOURCE_DATE_EPOCH="$(date -u -d "${VERSION//./-} 00:00:00" +%s)" \
  || { echo "keyring.version '$VERSION' is not a valid date" >&2; exit 1; }
export SOURCE_DATE_EPOCH

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
PKG_DIR="$OUT/_keyring-stage/${PACKAGE}_${VERSION}_all"
DEB_PATH="$OUT/${PACKAGE}_${VERSION}_all.deb"

echo "==> Staging $PACKAGE $VERSION"
rm -rf "$PKG_DIR"
mkdir -p "$PKG_DIR/DEBIAN" "$PKG_DIR/usr/share/keyrings" "$PKG_DIR/usr/share/doc/$PACKAGE"

# --- 1. The keyring --------------------------------------------------------------
# Binary, never armored: apt 2.4 to 2.8 answer NO_PUBKEY to an armored file named .gpg.
# --dearmor only decodes the base64, so the same certificate always gives the same bytes.
# A private, empty GNUPGHOME keeps gpg off the caller's keyrings.
gnupg_home="$(mktemp -d)"
chmod 700 "$gnupg_home"
GNUPGHOME="$gnupg_home" gpg --batch --dearmor < "$CERT" > "$PKG_DIR/usr/share/keyrings/$PACKAGE.gpg"
rm -rf "$gnupg_home"

# --- 2. Documentation -------------------------------------------------------------
cat > "$PKG_DIR/usr/share/doc/$PACKAGE/copyright" <<EOF2
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Orkeon
Source: $HOMEPAGE
Comment: The OpenPGP public certificate that signs the index of the Orkeon apt
 repository. Its fingerprint is published in SECURITY.md of the Orkeon
 repository.

Files: *
Copyright: 2024 Orkeon Contributors
License: MIT

License: MIT
 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:
 .
 The above copyright notice and this permission notice shall be included in all
 copies or substantial portions of the Software.
 .
 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 SOFTWARE.
EOF2

cat > "$PKG_DIR/usr/share/doc/$PACKAGE/changelog.Debian" <<EOF2
$PACKAGE ($VERSION) unstable; urgency=medium

  * Signing certificate of the Orkeon apt repository, as of $VERSION.

 -- $MAINTAINER  $(date -u -R -d "@$SOURCE_DATE_EPOCH")
EOF2
gzip -9n "$PKG_DIR/usr/share/doc/$PACKAGE/changelog.Debian"

# --- 3. Control -------------------------------------------------------------------
# Installed-Size as dpkg-gencontrol counts it (one KiB per directory, each file rounded up
# to the KiB), not `du`: the block size of the build filesystem must not reach the bytes.
INSTALLED_SIZE="$(find "$PKG_DIR" -path "$PKG_DIR/DEBIAN" -prune -o -mindepth 1 -printf '%y %s\n' \
  | awk '{ n += ($1 == "d") ? 1 : int(($2 + 1023) / 1024) } END { print n }')"
cat > "$PKG_DIR/DEBIAN/control" <<EOF2
Package: $PACKAGE
Version: $VERSION
Architecture: all
Multi-Arch: foreign
Section: misc
Priority: optional
Maintainer: $MAINTAINER
Installed-Size: $INSTALLED_SIZE
Homepage: $HOMEPAGE
Description: OpenPGP keyring of the Orkeon apt repository
 This package installs /usr/share/keyrings/orkeon-archive-keyring.gpg, the
 public certificate apt uses to verify the index of the Orkeon apt
 repository. Upgrading it carries the extension or the rotation of the
 signing key to every machine that uses the repository.
EOF2

# --- 4. Permissions and timestamps -------------------------------------------------
# Reset everything explicitly: the tree may sit on an NTFS mount where every file reads
# 0777, and dpkg-deb records whatever mode and time it finds. apt verifies as the user
# _apt, so the keyring must be world-readable.
find "$PKG_DIR" -type d -exec chmod 755 {} +
find "$PKG_DIR" -type f -exec chmod 644 {} +
find "$PKG_DIR" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +

# --- 5. Build -----------------------------------------------------------------------
rm -f "$DEB_PATH"
dpkg-deb --build --root-owner-group "$PKG_DIR" "$DEB_PATH"

# SHA256SUMS is shared with the other release artifacts: refresh our line, keep theirs.
(
  cd "$OUT"
  deb="$(basename "$DEB_PATH")"
  touch SHA256SUMS
  grep -v "  $deb\$" SHA256SUMS > SHA256SUMS.tmp || true
  sha256sum "$deb" >> SHA256SUMS.tmp
  sort -k2 SHA256SUMS.tmp > SHA256SUMS
  rm -f SHA256SUMS.tmp
)

if [[ "$KEEP_WORK" != true ]]; then
  rm -rf "$OUT/_keyring-stage"
fi

echo "==> Done: $DEB_PATH"
ls -l "$DEB_PATH"
