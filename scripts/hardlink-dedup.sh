#!/usr/bin/env bash
# Hardlinks identical files under a directory tree so archivers that preserve
# hard links (GNU tar, dpkg-deb) store each unique payload once.
#
# Why: every self-contained publish in an archive carries its own copy of the
# .NET runtime and of the shared Orkeon assemblies. gzip cannot deduplicate
# across files (32 KB window), so a linux tarball with three self-contained
# apps ships three byte-identical copies of ~everything. Hardlinking them at
# staging time is fully transparent: tar records the link, extraction restores
# it, and every file still reads the exact same bytes — nothing functional
# changes. Windows zips are not eligible (no hard-link concept) and are simply
# never passed to this script.
#
# Usage: scripts/hardlink-dedup.sh <root-dir>
# Prints a one-line summary (files linked, bytes saved).
set -euo pipefail

ROOT="${1:?usage: hardlink-dedup.sh <root-dir>}"
[[ -d "$ROOT" ]] || { echo "hardlink-dedup: not a directory: $ROOT" >&2; exit 2; }

# Group by SHA-256, keep the first path of each group as the canonical inode,
# re-link the rest. Only regular files above 4 KB are considered — below that
# the tar header overhead eats the gain. Pairs are TAB-delimited: mawk (the
# Debian default awk) silently cannot emit NUL bytes from printf, and .NET
# publish outputs never contain tabs or newlines in file names (spaces are
# fine with a tab delimiter).
find "$ROOT" -type f -size +4k -print0 \
  | xargs -0 sha256sum \
  | LC_ALL=C sort \
  | awk '
      { hash=$1; $1=""; sub(/^  ?/,""); path=$0 }
      hash==prev { printf "%s\t%s\n", keep, path; next }
      { prev=hash; keep=path }
    ' \
  | {
      linked=0
      saved=0
      while IFS=$'\t' read -r keep dup; do
        [[ -n "$dup" ]] || continue
        # Same inode already (earlier pass, or publisher-level linking): skip.
        if [[ "$keep" -ef "$dup" ]]; then continue; fi
        size=$(stat -c%s "$dup")
        ln -f "$keep" "$dup"
        linked=$((linked + 1))
        saved=$((saved + size))
      done
      echo "hardlink-dedup: $linked file(s) linked, $((saved / 1024 / 1024)) MB deduplicated under $ROOT"
    }
