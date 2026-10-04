#!/usr/bin/env bash
# Bench of scripts/apt/build-apt-index.sh, the generator of the signed apt index.
#
# Usage:
#   scripts/apt/test-build-apt-index.sh [--offline]   # default: fast, no network, no container
#   scripts/apt/test-build-apt-index.sh --containers  # real apt clients against a fake GitHub
#   scripts/apt/test-build-apt-index.sh --all         # both
#
# Everything is made at test time: a throwaway key (ed25519 certification-only primary,
# ed25519 signing subkey, OpenPGP v4) in a temporary GNUPGHOME, and tiny .deb files built
# with dpkg-deb (asset names without "~", Version fields with "~"). No real secret is read.
#
# --offline needs apt-ftparchive (apt-utils), dpkg-deb, gpg and gpgv; sqv is used when
# present. Without apt-ftparchive but with docker, it runs itself in a debian:13 container.
# It proves the index format: exact Release fields (no Codename, SHA256 only), Filename
# rewriting, determinism, the merge and its refusals (changed bytes, non-increasing Date,
# a signature that does not verify), the yank, the by-hash pruning (3 generations kept)
# and the Debian version order of every channel (stable, rc, dev).
#
# --containers needs docker only. A debian:13 container builds the index and plays
# GitHub: github.test answers /Orkeon/orkeon/raw/apt/<path> with a 302 to raw.test (the
# branch files) and /Orkeon/orkeon/releases/download/<tag>/<asset> with a 302 to
# objects.test (a long signed-looking URL), three host names on one internal network.
# A debian:13 client (apt 3, sqv) and an ubuntu:22.04 client (apt 2.4, gpgv) use the
# deb822 source the documentation gives, only the URI changed, and go through: an empty
# channel, a pinned install, upgrades rc.4 -> rc.5 -> 1.0.0 (a dev build sorting below
# the next rc), the keyring package taking over a hand-placed keyring, a yank, the
# stable channel, and the armored-keyring trap (NO_PUBKEY under apt 2.4). Every
# "apt-get update" must print no W:, E: or Err: line, and the fake GitHub must have
# served every request (no 404, no %7e, Packages fetched by-hash).
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
generator="$here/build-apt-index.sh"
self="$here/$(basename "$0")"
SERVER_IMAGE="debian:13"
CLIENT_IMAGES=("debian:13" "ubuntu:22.04")

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
exits()   { [ "$code" -eq "$1" ]; }
fails()   { [ "$code" -ne 0 ]; }
says()    { grep -qF -- "$1" <<<"$out"; }
no_line() { ! grep -qE -- "$1" <<<"$out"; }
finish() { # <name>
  if [ "$failed" -gt 0 ]; then echo "$1: ${failed} check(s) failed"; exit 1; fi
  echo "$1 passed"
}

# --- Fixtures: throwaway key and dummy packages ------------------------------------
PASSPHRASE="throwaway bench passphrase"
KEYRING_VERSION="2026.10.01"

make_key() { # <dir> <name>: <dir>/<name>.gpg (binary cert), .asc (armored), -secret.asc (subkey only)
  local dir="$1" name="$2" home fpr
  home="$(mktemp -d)"
  chmod 700 "$home"
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase "$PASSPHRASE" \
    --quick-gen-key "Orkeon Archive Test Key ($name) <test@orkeon.invalid>" ed25519 cert 2d 2>/dev/null
  fpr="$(GNUPGHOME="$home" gpg --batch --with-colons --list-keys 2>/dev/null | awk -F: '$1 == "fpr" { print $10; exit }')"
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase "$PASSPHRASE" \
    --quick-add-key "$fpr" ed25519 sign 2d 2>/dev/null
  GNUPGHOME="$home" gpg --batch --quiet --export "$fpr" > "$dir/$name.gpg" 2>/dev/null
  GNUPGHOME="$home" gpg --batch --quiet --armor --export "$fpr" > "$dir/$name.asc" 2>/dev/null
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase "$PASSPHRASE" \
    --armor --export-secret-subkeys "$fpr" > "$dir/$name-secret.asc" 2>/dev/null
  GNUPGHOME="$home" gpgconf --kill all >/dev/null 2>&1 || true
  rm -rf "$home"
}

make_deb() { # <out.deb> <package> <version> <arch> [salt]
  local out="$1" pkg="$2" ver="$3" arch="$4" salt="${5:-}" root
  root="$(mktemp -d)"
  mkdir -p "$root/DEBIAN"
  if [ "$pkg" = orkeon-archive-keyring ]; then
    mkdir -p "$root/usr/share/keyrings"
    cp "$fx/archive.gpg" "$root/usr/share/keyrings/orkeon-archive-keyring.gpg"
    chmod 0644 "$root/usr/share/keyrings/orkeon-archive-keyring.gpg"
  else
    mkdir -p "$root/usr/bin"
    printf '#!/bin/sh\necho "orkeon %s%s"\n' "$ver" "$salt" > "$root/usr/bin/orkeon"
    chmod 0755 "$root/usr/bin/orkeon"
  fi
  cat > "$root/DEBIAN/control" <<EOF
Package: $pkg
Version: $ver
Architecture: $arch
Maintainer: Orkeon Contributors <arion@orkeon.org>
Section: devel
Priority: optional
Description: Orkeon bench package (not a real build)
 Built by test-build-apt-index.sh to exercise the apt index generator.
EOF
  find "$root" -exec touch -h -d @1700000000 {} +
  SOURCE_DATE_EPOCH=1700000000 dpkg-deb --root-owner-group -Zgzip --build "$root" "$out" >/dev/null
  rm -rf "$root"
}

make_fixtures() { # sets $fx; builds keys and every .deb the scenarios use
  fx="$1"
  mkdir -p "$fx/debs"
  make_key "$fx" archive
  make_key "$fx" other
  local v a
  for v in 1.0.0~rc.3 1.0.0~rc.4 1.0.0~rc.5 1.0.0 1.0.0~rc.4.dev.9 1.0.0~rc.4.dev.12 1.0.0~rc.5.dev.1; do
    for a in amd64 arm64; do
      make_deb "$fx/debs/orkeon_${v//\~/-}_$a.deb" orkeon "$v" "$a"
    done
  done
  make_deb "$fx/debs/orkeon-archive-keyring_${KEYRING_VERSION}_all.deb" orkeon-archive-keyring "$KEYRING_VERSION" all
  make_deb "$fx/debs/changed_orkeon_1.0.0-rc.3_amd64.deb" orkeon 1.0.0~rc.3 amd64 " (rebuilt)"
}

# deb_arg <version> <arch> <tag> [asset]: one --deb argument for an orkeon package
deb_arg() {
  local file="$fx/debs/orkeon_${1//\~/-}_$2.deb"
  echo "--deb=$file=$3/${4:-$(basename "$file")}"
}
keyring_arg() { echo "--deb=$fx/debs/orkeon-archive-keyring_${KEYRING_VERSION}_all.deb=$1/orkeon-archive-keyring_${KEYRING_VERSION}_all.deb"; }

# gen <dir> <channel> [generator arguments...]: runs the generator, sets $out and $code
gen() {
  local dir="$1" channel="$2" args=()
  shift 2
  for a in "$@"; do
    case "$a" in --deb=*) args+=(--deb "${a#--deb=}") ;; *) args+=("$a") ;; esac
  done
  code=0
  out="$(APT_SIGNING_KEY="${SIGNING_KEY-$(cat "$fx/archive-secret.asc")}" \
         APT_SIGNING_PASSPHRASE="${SIGNING_PASSPHRASE-$PASSPHRASE}" \
         bash "$generator" --channel "$channel" --dir "$dir" \
           --keyring "${KEYRING-$fx/archive.gpg}" ${args[@]+"${args[@]}"} 2>&1)" || code=$?
}

# --- Offline part ----------------------------------------------------------------------
snapshot() { (cd "$1" && find . -type f -print0 | sort -z | xargs -0 sha256sum); }
release_fields() { sed -n 's/^\([A-Za-z0-9-]*\):.*/\1/p' "$1/Release" | tr '\n' ' '; }
field_of() { sed -n "s/^$2:[[:space:]]*//p" "$1/Release"; }
triplets() { awk '/^Package:/{p=$2} /^Version:/{v=$2} /^Architecture:/{a=$2} /^$/{print p, v, a}' "$1/Packages"; }
gpgv_ok() { gpgv --homedir "$work/gpgv-home" --keyring "$fx/archive.gpg" "$@" >/dev/null 2>&1; }
sha_listed() { # <dir> <file>: the Release lists the file's real SHA256 and size
  grep -qE "^ $(sha256sum "$1/$2" | cut -d' ' -f1) +$(stat -c %s "$1/$2") $2\$" "$1/Release"
}
stanza_sha_is_file() { # <dir> <asset path> <deb file>
  grep -A10 -xF "Filename: $2" "$1/Packages" | grep -qx "SHA256: $(sha256sum "$3" | cut -d' ' -f1)"
}
byhash_consistent() { # every by-hash file is named after its own SHA256
  local f
  for f in "$1"/by-hash/SHA256/*; do
    [ "$(basename "$f")" = generations ] && continue
    [ "$(sha256sum "$f" | cut -d' ' -f1)" = "$(basename "$f")" ] || return 1
  done
}
count_byhash() { find "$1/by-hash/SHA256" -type f ! -name generations | wc -l; }
tmp_clean() { [ -z "$(ls -A "$TMPDIR")" ]; }

offline_part() {
  local missing=() t
  for t in apt-ftparchive dpkg-deb gpg gpgv perl; do
    command -v "$t" >/dev/null 2>&1 || missing+=("$t")
  done
  if [ ${#missing[@]} -gt 0 ]; then
    if command -v docker >/dev/null 2>&1; then
      echo "# ${missing[*]} missing here: running the offline part in a $SERVER_IMAGE container"
      offline_in_docker
      return
    fi
    echo "test-build-apt-index: missing ${missing[*]} (apt-ftparchive is in apt-utils)" >&2
    exit 1
  fi

  work="$(mktemp -d)"
  trap 'GNUPGHOME="$work/gpgv-home" gpgconf --kill all >/dev/null 2>&1 || true; rm -rf "$work"' EXIT
  mkdir -p "$work/gpgv-home" "$work/tmp" "$work/apt"
  chmod 700 "$work/gpgv-home"
  make_fixtures "$work/fx"
  export TMPDIR="$work/tmp"
  local T=$(( $(date -u +%s) - 86400 ))
  local rc="$work/apt/rc" stable="$work/apt/stable" dev="$work/apt/dev" before

  echo "# an empty stable channel is a valid signed index"
  gen "$stable" stable --date-epoch "$T"
  check "exits 0" exits 0
  check "Packages is empty" test ! -s "$stable/Packages"
  check "Packages.gz decompresses to it" bash -c '[ -z "$(gzip -dc "$0/Packages.gz")" ]' "$stable"
  check "Release has exactly Origin, Label, Suite, Architectures, Acquire-By-Hash, Date, SHA256" \
    test "$(release_fields "$stable" | tr ' ' '\n' | sort | tr '\n' ' ')" = "Acquire-By-Hash Architectures Date Label Origin SHA256 Suite "
  check "Origin: Orkeon" test "$(field_of "$stable" Origin)" = Orkeon
  check "Label: Orkeon" test "$(field_of "$stable" Label)" = Orkeon
  check "Suite: raw/apt/stable (the source path, no trailing slash)" test "$(field_of "$stable" Suite)" = raw/apt/stable
  check "Architectures: amd64 arm64" test "$(field_of "$stable" Architectures)" = "amd64 arm64"
  check "Acquire-By-Hash: yes" test "$(field_of "$stable" Acquire-By-Hash)" = yes
  check "Date is the given clock" test "$(field_of "$stable" Date)" = "$(date -u -d "@$T" '+%a, %d %b %Y %H:%M:%S +0000')"
  check "no Codename, Valid-Until, MD5Sum, SHA1 or SHA512" bash -c '! grep -qE "^(Codename|Valid-Until|MD5Sum|SHA1|SHA512):" "$0/Release"' "$stable"
  check "SHA256 lists Packages" sha_listed "$stable" Packages
  check "SHA256 lists Packages.gz" sha_listed "$stable" Packages.gz
  check "SHA256 lists nothing else" test "$(grep -c '^ ' "$stable/Release")" -eq 2
  check "gpgv verifies InRelease" gpgv_ok "$stable/InRelease"
  check "gpgv verifies Release.gpg" gpgv_ok "$stable/Release.gpg" "$stable/Release"
  check "Release.gpg is armored" grep -q -- '-----BEGIN PGP SIGNATURE-----' "$stable/Release.gpg"
  check "InRelease signs exactly Release" bash -c 'gpgv --homedir "$1" --keyring "$2" --output - "$0/InRelease" 2>/dev/null | cmp -s - "$0/Release"' "$stable" "$work/gpgv-home" "$fx/archive.gpg"
  check "the signature uses SHA256" grep -qx 'Hash: SHA256' "$stable/InRelease"
  if command -v sqv >/dev/null 2>&1 && sqv --help 2>&1 | grep -q -- --cleartext; then
    check "sqv verifies InRelease" bash -c 'sqv --keyring "$1" --cleartext --output "$2" "$0/InRelease" >/dev/null 2>&1' "$stable" "$fx/archive.gpg" "$work/sqv.out"
    check "sqv verifies Release.gpg" bash -c 'sqv --keyring "$1" --signature-file "$0/Release.gpg" "$0/Release" >/dev/null 2>&1' "$stable" "$fx/archive.gpg"
  else
    echo "skip  sqv not installed"
  fi
  check "by-hash holds Packages and Packages.gz" test "$(count_byhash "$stable")" -eq 2
  check "by-hash files are named after their SHA256" byhash_consistent "$stable"
  check "the generator left no temporary file (GNUPGHOME destroyed)" tmp_clean

  echo "# rc, first generation: rc.3 and the keyring"
  gen "$rc" rc --date-epoch $((T + 10)) "$(deb_arg 1.0.0~rc.3 arm64 v1.0.0-rc.3)" \
    "$(deb_arg 1.0.0~rc.3 amd64 v1.0.0-rc.3)" "$(keyring_arg v1.0.0-rc.3)"
  check "exits 0" exits 0
  check "Suite: raw/apt/rc" test "$(field_of "$rc" Suite)" = raw/apt/rc
  check "Filename is releases/download/<tag>/<asset>" grep -qx 'Filename: releases/download/v1.0.0-rc.3/orkeon_1.0.0-rc.3_amd64.deb' "$rc/Packages"
  check "no Filename carries a ~" bash -c '! grep "^Filename:" "$0/Packages" | grep -qF "~"' "$rc"
  check "the stanza's SHA256 is the .deb's" stanza_sha_is_file "$rc" releases/download/v1.0.0-rc.3/orkeon_1.0.0-rc.3_amd64.deb "$fx/debs/orkeon_1.0.0-rc.3_amd64.deb"
  check "stanzas carry SHA256 and Size only (no MD5sum, SHA1, SHA512)" bash -c '! grep -qE "^(MD5sum|MD5Sum|SHA1|SHA512):" "$0/Packages" && [ "$(grep -c "^SHA256:" "$0/Packages")" -eq 3 ] && [ "$(grep -c "^Size:" "$0/Packages")" -eq 3 ]' "$rc"
  check "the Version keeps its ~" grep -qx 'Version: 1.0.0~rc.3' "$rc/Packages"
  check "Packages.gz is gzip -9n (no name, no time)" bash -c '[ "$(od -An -tx1 -j3 -N5 "$0/Packages.gz" | tr -d " ")" = "0000000000" ]' "$rc"
  check "Packages.gz decompresses to Packages" bash -c 'gzip -dc "$0/Packages.gz" | cmp -s - "$0/Packages"' "$rc"
  check "gpgv verifies InRelease" gpgv_ok "$rc/InRelease"

  echo "# determinism: same inputs, any order, same Packages and Release"
  gen "$work/apt/rc-twin" rc --date-epoch $((T + 10)) "$(keyring_arg v1.0.0-rc.3)" \
    "$(deb_arg 1.0.0~rc.3 amd64 v1.0.0-rc.3)" "$(deb_arg 1.0.0~rc.3 arm64 v1.0.0-rc.3)"
  check "exits 0" exits 0
  check "same Packages" cmp -s "$rc/Packages" "$work/apt/rc-twin/Packages"
  check "same Packages.gz" cmp -s "$rc/Packages.gz" "$work/apt/rc-twin/Packages.gz"
  check "same Release" cmp -s "$rc/Release" "$work/apt/rc-twin/Release"

  echo "# rc, second generation: rc.4 under its real dotted asset names, the keyring re-attached"
  gen "$rc" rc --date-epoch $((T + 20)) \
    "$(deb_arg 1.0.0~rc.4 amd64 v1.0.0-rc.4 orkeon_1.0.0.rc.4_amd64.deb)" \
    "$(deb_arg 1.0.0~rc.4 arm64 v1.0.0-rc.4 orkeon_1.0.0.rc.4_arm64.deb)" "$(keyring_arg v1.0.0-rc.4)"
  check "exits 0" exits 0
  check "the published asset name is taken as is" grep -qx 'Filename: releases/download/v1.0.0-rc.4/orkeon_1.0.0.rc.4_amd64.deb' "$rc/Packages"
  check "the same keyring bytes are reported unchanged" says "unchanged  orkeon-archive-keyring $KEYRING_VERSION all"
  check "... and keep their first Filename" grep -qx "Filename: releases/download/v1.0.0-rc.3/orkeon-archive-keyring_${KEYRING_VERSION}_all.deb" "$rc/Packages"
  check "history is kept: rc.3 still listed" grep -qx 'Version: 1.0.0~rc.3' "$rc/Packages"

  echo "# refusals leave the channel untouched"
  before="$(snapshot "$rc")"
  gen "$rc" rc --date-epoch $((T + 30)) --deb "$fx/debs/changed_orkeon_1.0.0-rc.3_amd64.deb=v1.0.0-rc.3/orkeon_1.0.0-rc.3_amd64.deb"
  check "a published triplet with other bytes is refused" fails
  check "... with an explicit message" says "refused: orkeon 1.0.0~rc.3 amd64 is already published"
  check "... and nothing written" test "$(snapshot "$rc")" = "$before"
  gen "$rc" rc --date-epoch $((T + 20))
  check "the same Date again is refused" fails
  check "... with an explicit message" says "is not strictly after the published Date"
  gen "$rc" rc --date-epoch $((T + 5))
  check "an older Date is refused" fails
  check "... and nothing written" test "$(snapshot "$rc")" = "$before"
  KEYRING="$fx/other.gpg" gen "$rc" rc --date-epoch $((T + 30))
  check "a signature that does not verify against --keyring is refused" fails
  check "... with an explicit message" says "does not verify against"
  check "... and nothing written" test "$(snapshot "$rc")" = "$before"
  KEYRING="$fx/archive.asc" gen "$rc" rc --date-epoch $((T + 30))
  check "an armored keyring is refused (NO_PUBKEY under apt 2.4-2.8)" says "is armored"
  SIGNING_PASSPHRASE="wrong" gen "$rc" rc --date-epoch $((T + 30))
  check "a wrong passphrase fails" fails
  SIGNING_KEY="" gen "$rc" rc --date-epoch $((T + 30))
  check "an empty APT_SIGNING_KEY fails" fails
  gen "$rc" rc --date-epoch $((T + 30)) --deb "$fx/debs/orkeon_1.0.0-rc.5_amd64.deb=v1.0.0-rc.5/orkeon_1.0.0~rc.5_amd64.deb"
  check "an asset name with ~ is refused" says "carries a '~'"
  gen "$rc" rc --date-epoch $((T + 30)) --deb "$fx/debs/orkeon_1.0.0-rc.5_amd64.deb=v1.0.0-rc.5/orkeon_1.0.0-rc.5_arm64.deb"
  check "an asset named for another architecture is refused" says "does not name orkeon for amd64"
  gen "$rc" rc --date-epoch $((T + 30)) --yank orkeon=9.9.9
  check "a yank that matches nothing is refused" fails
  gen "$rc" rc --date-epoch $((T + 30)) "$(deb_arg 1.0.0~rc.5 amd64 v1.0.0-rc.5)" --yank orkeon=1.0.0~rc.5
  check "a yank of what the same run adds is refused" fails
  gen "$rc" beta --date-epoch $((T + 30))
  check "an unknown channel exits 2" exits 2
  gen "$rc" rc --frobnicate
  check "an unknown argument exits 2" exits 2
  check "... and nothing written by any refusal" test "$(snapshot "$rc")" = "$before"
  gen "$stable" stable --date-epoch $((T + 30)) "$(deb_arg 1.0.0~rc.4 amd64 v1.0.0-rc.4)"
  check "stable refuses a prerelease" says "the stable channel takes final versions only"
  check "the generator left no temporary file" tmp_clean

  echo "# rc, third generation: 1.0.0 passed first, sorted by Debian version order"
  gen "$rc" rc --date-epoch $((T + 40)) "$(deb_arg 1.0.0 arm64 v1.0.0)" "$(deb_arg 1.0.0 amd64 v1.0.0)" \
    "$(deb_arg 1.0.0~rc.5 amd64 v1.0.0-rc.5)" "$(deb_arg 1.0.0~rc.5 arm64 v1.0.0-rc.5)"
  check "exits 0" exits 0
  check "order: package, then version (rc.3 < rc.4 < rc.5 < 1.0.0), then architecture" test "$(triplets "$rc" | tr '\n' ',')" = \
"orkeon 1.0.0~rc.3 amd64,orkeon 1.0.0~rc.3 arm64,orkeon 1.0.0~rc.4 amd64,orkeon 1.0.0~rc.4 arm64,orkeon 1.0.0~rc.5 amd64,orkeon 1.0.0~rc.5 arm64,orkeon 1.0.0 amd64,orkeon 1.0.0 arm64,orkeon-archive-keyring $KEYRING_VERSION all,"
  gen "$stable" stable --date-epoch $((T + 40)) "$(deb_arg 1.0.0 amd64 v1.0.0)" "$(deb_arg 1.0.0 arm64 v1.0.0)" "$(keyring_arg v1.0.0)"
  check "stable takes the final version" test "$(triplets "$stable" | tr '\n' ',')" = \
"orkeon 1.0.0 amd64,orkeon 1.0.0 arm64,orkeon-archive-keyring $KEYRING_VERSION all,"

  echo "# yank"
  gen "$rc" rc --date-epoch $((T + 50)) --yank orkeon=1.0.0~rc.4
  check "exits 0" exits 0
  check "every architecture of the version is logged as yanked" bash -c '[ "$(grep -c "^yanked     orkeon 1.0.0~rc.4 " <<<"$0")" -eq 2 ]' "$out"
  check "... and gone from Packages" bash -c '! grep -qx "Version: 1.0.0~rc.4" "$0/Packages"' "$rc"
  gen "$rc" rc --date-epoch $((T + 60)) --yank orkeon=1.0.0~rc.3/arm64
  check "a yank with /arch removes that architecture only" test "$(triplets "$rc" | grep -c '^orkeon 1.0.0~rc.3 ')" -eq 1
  check "the re-signed index verifies" gpgv_ok "$rc/InRelease"

  echo "# by-hash: our own pruning keeps the last 3 generations"
  check "5 generations made, 3 listed" test "$(wc -l < "$rc/by-hash/SHA256/generations")" -eq 3
  check "6 files kept (Packages and Packages.gz of each)" test "$(count_byhash "$rc")" -eq 6
  check "by-hash files are named after their SHA256" byhash_consistent "$rc"
  check "the current Packages is there" test -f "$rc/by-hash/SHA256/$(sha256sum "$rc/Packages" | cut -d' ' -f1)"
  check "the current Packages.gz is there" test -f "$rc/by-hash/SHA256/$(sha256sum "$rc/Packages.gz" | cut -d' ' -f1)"
  check "the first generation is pruned" test ! -e "$rc/by-hash/SHA256/$(sha256sum "$work/apt/rc-twin/Packages" | cut -d' ' -f1)"
  check "pruning is logged" says "pruned     by-hash/SHA256/"
  touch -d @1 "$rc/by-hash/SHA256/"*
  before="$(cat "$rc/by-hash/SHA256/generations")"
  gen "$rc" rc --date-epoch $((T + 70))
  check "a plain re-signature exits 0" exits 0
  check "... adds no generation" test "$(cat "$rc/by-hash/SHA256/generations")" = "$before"
  check "... prunes nothing whatever the mtimes" test "$(count_byhash "$rc")" -eq 6
  check "... with a newer Date" test "$(field_of "$rc" Date)" = "$(date -u -d "@$((T + 70))" '+%a, %d %b %Y %H:%M:%S +0000')"

  echo "# dev channel: dev builds sort below the next rc"
  gen "$dev" dev --date-epoch "$T" "$(deb_arg 1.0.0~rc.5.dev.1 amd64 apt-dev)" \
    "$(deb_arg 1.0.0~rc.4.dev.12 amd64 apt-dev)" "$(deb_arg 1.0.0~rc.4.dev.9 amd64 apt-dev)" "$(keyring_arg apt-dev)"
  check "exits 0" exits 0
  check "Suite: raw/apt/dev" test "$(field_of "$dev" Suite)" = raw/apt/dev
  check "Filename on the fixed apt-dev prerelease" grep -qx 'Filename: releases/download/apt-dev/orkeon_1.0.0-rc.4.dev.12_amd64.deb' "$dev/Packages"
  check "order: dev.9 < dev.12 < rc.5.dev.1" test "$(triplets "$dev" | tr '\n' ',')" = \
"orkeon 1.0.0~rc.4.dev.9 amd64,orkeon 1.0.0~rc.4.dev.12 amd64,orkeon 1.0.0~rc.5.dev.1 amd64,orkeon-archive-keyring $KEYRING_VERSION all,"
  check "dpkg: 1.0.0~rc.4.dev.12 < 1.0.0~rc.5 < 1.0.0" bash -c 'dpkg --compare-versions 1.0.0~rc.4.dev.12 lt 1.0.0~rc.5 && dpkg --compare-versions 1.0.0~rc.5 lt 1.0.0'

  echo "# without --date-epoch the Date is the clock"
  gen "$work/apt/clock" rc
  check "exits 0" exits 0
  check "Date within a minute of now" bash -c 'd=$(date -u -d "$(sed -n "s/^Date: //p" "$0/Release")" +%s); n=$(date -u +%s); [ $((n - d)) -ge 0 ] && [ $((n - d)) -lt 60 ]' "$work/apt/clock"
  before="$(field_of "$work/apt/clock" Date)"
  gen "$work/apt/clock" rc
  check "an immediate second run is refused or carries a later Date, never the same one" \
    bash -c '[ "$0" -ne 0 ] || [ "$(date -u -d "$(sed -n "s/^Date: //p" "$1/Release")" +%s)" -gt "$(date -u -d "$2" +%s)" ]' "$code" "$work/apt/clock" "$before"
  check "the generator left no temporary file" tmp_clean

  finish "test-build-apt-index (offline)"
}

# dexec <container> <command...>: runs the command in the container to completion, prints
# its output, returns its status. The command runs detached and is polled through
# `docker cp`: some docker CLI/daemon pairings drop the attached stream of an exec that
# lasts more than a second, and return before the command ends.
dexec() {
  local c="$1" tag="/tmp/dexec-$RANDOM$RANDOM" waited=0 st
  shift
  docker exec -d "$c" bash -c '"$@" > "$0.out" 2>&1; echo $? > "$0.rc"' "$tag" "$@"
  until st="$(docker cp "$c:$tag.rc" - 2>/dev/null | tar -xOf - 2>/dev/null)" && [ -n "$st" ]; do
    waited=$((waited + 1))
    [ "$waited" -lt 1800 ] || { echo "test-build-apt-index: timed out in $c: $*" >&2; return 124; }
    sleep 1
  done
  docker cp "$c:$tag.out" - | tar -xOf -
  return "$st"
}

offline_in_docker() {
  local name="apt-index-offline-$$"
  # shellcheck disable=SC2064
  trap "docker rm -f $name >/dev/null 2>&1 || true" EXIT
  docker run -d --name "$name" "$SERVER_IMAGE" sleep infinity >/dev/null
  dexec "$name" bash -c 'apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends apt-utils gpg gpg-agent gpgv sqv >/dev/null' \
    || { echo "test-build-apt-index: could not install apt-utils in $SERVER_IMAGE" >&2; exit 1; }
  docker cp "$here/." "$name:/bench"
  dexec "$name" bash /bench/test-build-apt-index.sh --offline
}

# --- Container part: the fake GitHub ---------------------------------------------------
write_fake_github() { # <file>
  cat > "$1" <<'PY'
"""Fake GitHub for the apt bench: three host names, two cross-host 302 redirects."""
import hashlib
import os
import re
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

BRANCH = "/srv/branch"   # the files of the apt branch
ASSETS = "/srv/assets"   # <tag>/<asset>, the Release assets
LOG = "/srv/requests.log"


def token(tag, asset):
    return hashlib.sha256(f"{tag}/{asset}".encode()).hexdigest()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def reply(self, status, headers=None, path=None, body=True):
        size = os.path.getsize(path) if path else 0
        self.send_response(status)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Length", str(size))
        if path:
            self.send_header("Content-Type", "application/octet-stream")
        self.end_headers()
        if path and body:
            with open(path, "rb") as f:
                self.wfile.write(f.read())
        with open(LOG, "a") as f:
            f.write(f"{status} {self.headers.get('Host', '')} {self.path}\n")

    def safe(self, root, rel):
        full = os.path.realpath(os.path.join(root, rel))
        return full if full.startswith(root + "/") and os.path.isfile(full) else None

    def serve(self, body):
        host = self.headers.get("Host", "").split(":")[0]
        raw_path = self.path.split("?", 1)[0]
        path = urllib.parse.unquote(raw_path)
        if host == "github.test":
            m = re.fullmatch(r"/Orkeon/orkeon/raw/apt/(.+)", raw_path)
            if m:
                return self.reply(302, {"Location": f"http://raw.test/Orkeon/orkeon/apt/{m.group(1)}"})
            m = re.fullmatch(r"/Orkeon/orkeon/releases/download/([^/]+)/([^/]+)", path)
            if m and self.safe(ASSETS, f"{m.group(1)}/{m.group(2)}"):
                tag, asset = m.groups()
                q = urllib.parse.urlencode({
                    "X-Amz-Algorithm": "AWS4-HMAC-SHA256",
                    "X-Amz-Credential": "FAKEKEY/20261004/us-east-1/s3/aws4_request",
                    "X-Amz-Date": "20261004T000000Z",
                    "X-Amz-Expires": "300",
                    "X-Amz-Signature": hashlib.sha256(asset.encode()).hexdigest(),
                    "X-Amz-SignedHeaders": "host",
                    "actor_id": "0",
                    "key_id": "0",
                    "repo_id": "123456789",
                    "response-content-disposition": f"attachment; filename={asset}",
                    "response-content-type": "application/octet-stream",
                }, quote_via=urllib.parse.quote)
                loc = f"http://objects.test/github-production-release-asset-2e65be/123456789/{token(tag, asset)}?{q}"
                return self.reply(302, {"Location": loc})
        elif host == "raw.test":
            m = re.fullmatch(r"/Orkeon/orkeon/apt/(.+)", path)
            full = m and self.safe(BRANCH, m.group(1))
            if full:
                return self.reply(200, path=full, body=body)
        elif host == "objects.test":
            m = re.fullmatch(r"/github-production-release-asset-2e65be/123456789/([0-9a-f]{64})", path)
            if m:
                for tag in sorted(os.listdir(ASSETS)):
                    for asset in sorted(os.listdir(os.path.join(ASSETS, tag))):
                        if token(tag, asset) == m.group(1):
                            return self.reply(200, path=os.path.join(ASSETS, tag, asset), body=body)
        return self.reply(404)

    def do_GET(self):
        self.serve(True)

    def do_HEAD(self):
        self.serve(False)


ThreadingHTTPServer(("0.0.0.0", 80), Handler).serve_forever()
PY
}

# Inside the server container: fixtures, assets and the published channels.
bench_server() { # <step>
  fx=/srv/fx
  publish() { # <tag> <asset> <file>: attach an asset to a fake Release
    mkdir -p "/srv/assets/$1"
    cp "$3" "/srv/assets/$1/$2"
  }
  pub() { # <channel> [generator arguments]: the generator against the branch, from the clock
    local channel="$1"
    shift
    gen "/srv/branch/$channel" "$channel" "$@"
    echo "$out"
    [ "$code" -eq 0 ] || { echo "bench server: publishing $channel failed" >&2; exit 1; }
  }
  orkeon() { # <version> <tag> [asset name pattern]: both architectures, attached and indexed
    local a file asset
    for a in amd64 arm64; do
      file="$fx/debs/orkeon_${1//\~/-}_$a.deb"
      asset="$(basename "$file")"
      publish "$2" "$asset" "$file"
      printf '%s\n' "--deb=$file=$2/$asset"
    done
  }
  keyring() { # <tag>
    local file="$fx/debs/orkeon-archive-keyring_${KEYRING_VERSION}_all.deb"
    publish "$1" "$(basename "$file")" "$file"
    keyring_arg "$1"
  }
  case "$1" in
    init)
      mkdir -p /srv/branch /srv/assets /srv/kit
      make_fixtures "$fx"
      write_fake_github /srv/fake-github.py
      cp "$fx/archive.gpg" "$fx/archive.asc" /srv/kit/
      mapfile -t rc < <(orkeon 1.0.0~rc.3 v1.0.0-rc.3; keyring v1.0.0-rc.3; orkeon 1.0.0~rc.4 v1.0.0-rc.4)
      mapfile -t dev < <(orkeon 1.0.0~rc.4.dev.12 apt-dev; keyring apt-dev)
      pub stable
      pub rc "${rc[@]}"
      pub dev "${dev[@]}"
      ;;
    rc5)
      mapfile -t rc < <(orkeon 1.0.0~rc.5 v1.0.0-rc.5)
      pub rc "${rc[@]}"
      ;;
    final)
      mapfile -t rc < <(orkeon 1.0.0 v1.0.0)
      mapfile -t st < <(orkeon 1.0.0 v1.0.0; keyring v1.0.0)
      pub rc "${rc[@]}"
      pub stable "${st[@]}"
      ;;
    yank)
      pub rc --yank orkeon=1.0.0~rc.4
      ;;
    *) echo "bench server: unknown step $1" >&2; exit 2 ;;
  esac
}

# Inside a client container: one step of the user's journey.
bench_client() { # <step>
  export DEBIAN_FRONTEND=noninteractive
  local keyring=/usr/share/keyrings/orkeon-archive-keyring.gpg
  sources() { # <channel>...: the documented deb822 source, URI pointed at the fake GitHub
    rm -f /etc/apt/sources.list.d/orkeon*.sources
    local c
    for c in "$@"; do
      cat > "/etc/apt/sources.list.d/orkeon-$c.sources" <<EOF
Types: deb
URIs: http://github.test/Orkeon/orkeon/
Suites: raw/apt/$c/
Include: orkeon orkeon-archive-keyring
Signed-By: ${SIGNED_BY:-$keyring}
EOF
    done
  }
  update() {
    code=0
    out="$(apt-get update 2>&1)" || code=$?
    echo "$out" | sed 's/^/      | /'
  }
  update_clean() { # <label>
    update
    check "$1: apt-get update exits 0" exits 0
    check "$1: no W: line" no_line '^W:'
    check "$1: no E: line" no_line '^E:'
    check "$1: no Err: line" no_line '^Err:'
  }
  apt_do() { code=0; out="$(apt-get -y "$@" </dev/null 2>&1)" || code=$?; [ "$code" -eq 0 ] || echo "$out" | sed 's/^/      | /'; }
  installed() { [ "$(dpkg-query -W -f '${Version}' "$1" 2>/dev/null)" = "$2" ]; }
  madison() { apt-cache madison orkeon | awk -F'|' '{gsub(/ /, "", $2); print $2}' | sort -u | tr '\n' ' '; }

  case "$1" in
    start)
      rm -f /etc/apt/sources.list /etc/apt/sources.list.d/*
      echo 'Acquire::Retries "0";' > /etc/apt/apt.conf.d/99bench
      # The keyring as the documentation installs it by hand, before the package exists.
      install -m 0644 /bench/kit/archive.gpg "$keyring"
      sources stable
      update_clean "empty stable"
      check "empty stable: no orkeon offered" test -z "$(madison)"
      sources rc
      update_clean "rc"
      apt_do install orkeon=1.0.0~rc.3
      check "install orkeon=1.0.0~rc.3 installs the pinned version" installed orkeon 1.0.0~rc.3
      check "... whose binary runs" bash -c '[ "$(orkeon)" = "orkeon 1.0.0~rc.3" ]'
      apt_do install orkeon
      check "install orkeon upgrades to the newest rc (1.0.0~rc.4)" installed orkeon 1.0.0~rc.4
      sources dev
      update_clean "dev"
      apt_do install --allow-downgrades orkeon=1.0.0~rc.4.dev.12
      check "the dev channel installs 1.0.0~rc.4.dev.12" installed orkeon 1.0.0~rc.4.dev.12
      ;;
    rc5)
      sources dev rc
      update_clean "dev + rc"
      apt_do upgrade
      check "upgrade moves 1.0.0~rc.4.dev.12 to 1.0.0~rc.5 (the dev build sorts below the next rc)" installed orkeon 1.0.0~rc.5
      ;;
    final)
      sources rc
      update_clean "rc with 1.0.0"
      apt_do upgrade
      check "upgrade moves 1.0.0~rc.5 to 1.0.0 (a final sorts above its rcs)" installed orkeon 1.0.0
      apt_do install orkeon-archive-keyring
      check "the keyring package installs over the hand-placed file, no question asked" exits 0
      check "... and owns it" bash -c 'dpkg -S "$0" 2>/dev/null | grep -q "^orkeon-archive-keyring:"' "$keyring"
      check "... in 0644, readable by _apt" test "$(stat -c %a "$keyring")" = 644
      update_clean "rc after the keyring takeover"
      ;;
    yank)
      update_clean "rc after the yank"
      check "the yanked 1.0.0~rc.4 is no longer offered" test "$(madison)" = "1.0.0 1.0.0~rc.3 1.0.0~rc.5 "
      check "apt-cache policy agrees" bash -c '! apt-cache policy orkeon | grep -qF "1.0.0~rc.4 "'
      sources stable
      update_clean "stable"
      check "stable offers 1.0.0 only" test "$(madison)" = "1.0.0 "
      if [ "${CLIENT_IMAGE:-}" = "ubuntu:22.04" ]; then
        install -m 0644 /bench/kit/archive.asc /usr/share/keyrings/orkeon-armored.gpg
        SIGNED_BY=/usr/share/keyrings/orkeon-armored.gpg sources stable
        update
        check "trap documented: an armored keyring named .gpg gives NO_PUBKEY under apt 2.4" says "NO_PUBKEY"
        rm -f /usr/share/keyrings/orkeon-armored.gpg
        sources stable
      fi
      ;;
    *) echo "bench client: unknown step $1" >&2; exit 2 ;;
  esac
  finish "client $CLIENT_IMAGE, step $1"
}

containers_part() {
  command -v docker >/dev/null 2>&1 || { echo "test-build-apt-index: --containers needs docker" >&2; exit 1; }
  local ip image name step
  # Globals: the EXIT trap removes them after this function has returned.
  bench_net="apt-bench-$$"
  srv="apt-bench-server-$$"
  clients=()
  cleanup_containers() {
    docker rm -f "$srv" ${clients[@]+"${clients[@]}"} >/dev/null 2>&1 || true
    docker network rm "$bench_net" >/dev/null 2>&1 || true
  }
  trap cleanup_containers EXIT

  echo "# fake GitHub in $SERVER_IMAGE"
  docker network create --internal "$bench_net" >/dev/null
  docker run -d --name "$srv" "$SERVER_IMAGE" sleep infinity >/dev/null
  dexec "$srv" bash -c 'apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends apt-utils gpg gpg-agent gpgv sqv python3 >/dev/null' \
    || { echo "test-build-apt-index: could not prepare the server container" >&2; exit 1; }
  docker cp "$here/." "$srv:/bench"
  docker network connect "$bench_net" "$srv"
  ip="$(docker inspect -f "{{(index .NetworkSettings.Networks \"$bench_net\").IPAddress}}" "$srv")"
  dexec "$srv" bash /bench/test-build-apt-index.sh --bench-server init
  docker exec -d "$srv" python3 /srv/fake-github.py
  dexec "$srv" python3 -c '
import time, urllib.request
for _ in range(50):
    try:
        urllib.request.urlopen("http://127.0.0.1/", timeout=1)
    except urllib.error.HTTPError:
        break
    except OSError:
        time.sleep(0.2)
'

  for image in "${CLIENT_IMAGES[@]}"; do
    name="apt-bench-client-${image//[:.]/-}-$$"
    clients+=("$name")
    docker run -d --name "$name" --network "$bench_net" -e "CLIENT_IMAGE=$image" \
      --add-host "github.test:$ip" --add-host "raw.test:$ip" --add-host "objects.test:$ip" \
      "$image" sleep infinity >/dev/null
    docker cp "$here/." "$name:/bench"
    docker cp "$srv:/srv/kit" - | docker cp - "$name:/bench"
  done

  for step in start rc5 final yank; do
    if [ "$step" != start ]; then
      echo "# server publishes: $step"
      dexec "$srv" bash /bench/test-build-apt-index.sh --bench-server "$step" | sed 's/^/  /'
    fi
    for name in "${clients[@]}"; do
      echo "# client $name: $step"
      dexec "$name" bash /bench/test-build-apt-index.sh --bench-client "$step" || failed=$((failed + 1))
    done
  done

  echo "# what the fake GitHub served"
  out="$(docker cp "$srv:/srv/requests.log" - | tar -xOf -)"
  echo "$out" | sed 's/^/      | /'
  check "every request was served (no 404)" no_line '^404 (github|raw|objects)\.test '
  check "apt never asked for a %7e" bash -c '! grep -qi "%7e" <<<"$0"' "$out"
  check "the index came through the github.test -> raw.test redirect" says "302 github.test /Orkeon/orkeon/raw/apt/rc/InRelease"
  check "Packages came by-hash (SHA256)" says "200 raw.test /Orkeon/orkeon/apt/rc/by-hash/SHA256/"
  check "never by-hash/SHA512" bash -c '! grep -q "SHA512" <<<"$0"' "$out"
  check "packages came from releases/download/<tag>/<asset>" says "302 github.test /Orkeon/orkeon/releases/download/v1.0.0-rc.3/orkeon_1.0.0-rc.3_amd64.deb"
  check "... through the objects.test redirect" says "200 objects.test /github-production-release-asset-2e65be/"
  check "the dev build came from the apt-dev prerelease" says "302 github.test /Orkeon/orkeon/releases/download/apt-dev/orkeon_1.0.0-rc.4.dev.12_amd64.deb"
  finish "test-build-apt-index (containers)"
}

case "${1:---offline}" in
  --offline) offline_part ;;
  --containers) containers_part ;;
  --all) bash "$self" --offline && bash "$self" --containers ;;
  --bench-server) bench_server "$2" ;;
  --bench-client) bench_client "$2" ;;
  -h|--help) sed -n '2,29p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) echo "test-build-apt-index: unknown argument: $1" >&2; exit 2 ;;
esac
