#!/usr/bin/env bash
# Bench of scripts/apt/check-apt-snippet.sh on throw-away copies of the two pages and of
# installers/apt. Offline, gpg only.
#
# Usage: scripts/apt/test-check-apt-snippet.sh
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
guard="$here/check-apt-snippet.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
tree() { # a fresh copy of what the guard reads; sets $t
  t="$work/t$RANDOM"
  mkdir -p "$t/docs/guides" "$t/docs/fr/guides" "$t/installers/apt"
  cp "$repo/docs/guides/install-with-apt.md" "$t/docs/guides/"
  cp "$repo/docs/fr/guides/install-with-apt.md" "$t/docs/fr/guides/"
  cp "$repo/installers/apt/orkeon.sources" "$repo/installers/apt/orkeon-archive-keyring.asc" "$t/installers/apt/"
}
passes() { bash "$guard" --root "$t" >/dev/null 2>&1; }
fails()  { ! passes; }
both()   { sed -i "$1" "$t/docs/guides/install-with-apt.md" "$t/docs/fr/guides/install-with-apt.md"; }
sha_of() { gpg --batch --dearmor < "$t/installers/apt/orkeon-archive-keyring.asc" | sha256sum | cut -d' ' -f1; }

tree
check "the committed pages and installers/apt pass" passes
tree
sed -i 's|^sudo apt-get update && sudo apt-get install -y orkeon orkeon-archive-keyring$|sudo apt-get update \&\& sudo apt-get install -y orkeon|' "$t/docs/fr/guides/install-with-apt.md"
check "a French block that differs fails" fails
tree
both 's|^Suites: raw/apt/stable/$|Suites: raw/apt/rc/|'
check "a source other than installers/apt/orkeon.sources fails (in both pages)" fails
tree
both 's|^Include: .*$|Include: orkeon|'
check "... and so does a changed Include: line" fails
tree
both 's|/raw/apt/orkeon-archive-keyring.gpg|/raw/main/orkeon-archive-keyring.gpg|'
check "a key downloaded from elsewhere fails" fails
tree
both '/apt-setup:end/d'
check "a page without its end marker fails" fails

echo "# once the real certificate is committed (no PLACEHOLDER header)"
tree
sed -i '/^Comment: PLACEHOLDER/d' "$t/installers/apt/orkeon-archive-keyring.asc"
# The pages as they read before the ceremony, whatever the committed state.
both 's|^echo "[^ ]*  /tmp/orkeon-archive-keyring.gpg"|echo "<PENDING-KEY-CEREMONY>  /tmp/orkeon-archive-keyring.gpg"|'
check "the pending value fails" fails
both "s|<PENDING-KEY-CEREMONY>|$(sha_of)|"
check "the keyring's SHA-256 passes" passes
both "s|$(sha_of)|$(printf '0%.0s' $(seq 64))|"
check "another SHA-256 fails" fails

if [ "$failed" -gt 0 ]; then echo "test-check-apt-snippet: ${failed} check(s) failed"; exit 1; fi
echo "test-check-apt-snippet passed"
