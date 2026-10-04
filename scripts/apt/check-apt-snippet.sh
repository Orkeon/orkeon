#!/usr/bin/env bash
# Guard of the apt installation block: the one users copy, and the one verify-apt-repo.sh
# replays word for word. Offline, no secret.
#
# Usage: scripts/apt/check-apt-snippet.sh [--root DIR]
#
# The block sits between "<!-- apt-setup:begin -->" and "<!-- apt-setup:end -->" in
# docs/guides/install-with-apt.md and docs/fr/guides/install-with-apt.md. It fails when:
#   - either page lacks the block, or the two blocks differ by a single byte;
#   - the source the block writes (its <<'EOF' ... EOF here-document) is not exactly
#     installers/apt/orkeon.sources;
#   - the block does not download https://github.com/Orkeon/orkeon/raw/apt/orkeon-archive-keyring.gpg;
#   - the SHA-256 its `sha256sum --check` line expects is not the one of the binary keyring
#     made from installers/apt/orkeon-archive-keyring.asc by `gpg --dearmor` (the file at
#     the root of the apt branch). While that certificate is the development placeholder
#     (armor header "Comment: PLACEHOLDER ..."), the value <PENDING-KEY-CEREMONY> is
#     accepted; once the real certificate is committed, it is not.
# --root DIR checks another tree (tests).
set -euo pipefail
export LC_ALL=C

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --root) [[ $# -ge 2 ]] || { echo "check-apt-snippet: --root needs a value" >&2; exit 2; }; ROOT="$2"; shift 2 ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "check-apt-snippet: unknown argument: $1" >&2; exit 2 ;;
  esac
done

EN="$ROOT/docs/guides/install-with-apt.md"
FR="$ROOT/docs/fr/guides/install-with-apt.md"
SOURCES="$ROOT/installers/apt/orkeon.sources"
CERT="$ROOT/installers/apt/orkeon-archive-keyring.asc"
KEY_URL="https://github.com/Orkeon/orkeon/raw/apt/orkeon-archive-keyring.gpg"
PENDING="<PENDING-KEY-CEREMONY>"

errors=0
fail() { echo "error: $*" >&2; errors=$((errors + 1)); }
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

block() { # <page> <out>: the lines between the markers, the code fence included
  awk '/<!-- apt-setup:begin -->/ { on = 1; n++; next } /<!-- apt-setup:end -->/ { on = 0; next } on { print } END { exit n == 1 ? 0 : 1 }' "$1" > "$2"
}

for f in "$EN" "$FR" "$SOURCES" "$CERT"; do
  [[ -f "$f" ]] || { echo "error: missing ${f#"$ROOT"/}" >&2; exit 1; }
done
block "$EN" "$work/en" || fail "${EN#"$ROOT"/} must hold exactly one block between <!-- apt-setup:begin --> and <!-- apt-setup:end -->"
block "$FR" "$work/fr" || fail "${FR#"$ROOT"/} must hold exactly one block between <!-- apt-setup:begin --> and <!-- apt-setup:end -->"
[[ -s "$work/en" ]] || { echo "error: the English block is empty" >&2; exit 1; }
if ! cmp -s "$work/en" "$work/fr"; then
  diff -u "$work/en" "$work/fr" | sed 's/^/    /' >&2 || true
  fail "the English and French blocks differ: they must be identical, byte for byte"
fi

# The source the block writes: the here-document after "<<'EOF'".
awk "/<<'EOF'\$/ { on = 1; next } on && /^EOF\$/ { on = 0; exit } on { print }" "$work/en" > "$work/sources"
if [[ ! -s "$work/sources" ]]; then
  fail "the block writes no source (no <<'EOF' ... EOF here-document)"
elif ! cmp -s "$work/sources" "$SOURCES"; then
  diff -u "$SOURCES" "$work/sources" | sed 's/^/    /' >&2 || true
  fail "the source the block writes is not installers/apt/orkeon.sources"
fi

grep -qF "$KEY_URL" "$work/en" || fail "the block does not download $KEY_URL"

expected="$(sed -n 's|^echo "\([^ ]*\)  /tmp/orkeon-archive-keyring.gpg" \| sha256sum --check$|\1|p' "$work/en")"
home="$(mktemp -d)"
actual="$(GNUPGHOME="$home" gpg --batch --dearmor < "$CERT" | sha256sum | cut -d' ' -f1)"
rm -rf "$home"
placeholder=false
grep -q '^Comment: PLACEHOLDER' "$CERT" && placeholder=true
if [[ -z "$expected" ]]; then
  fail "the block has no 'echo \"<sha256>  /tmp/orkeon-archive-keyring.gpg\" | sha256sum --check' line"
elif [[ "$expected" == "$actual" ]]; then
  :
elif [[ "$expected" == "$PENDING" ]] && $placeholder; then
  echo "notice: the block's key SHA-256 is $PENDING; accepted while the certificate is the placeholder."
elif [[ "$expected" == "$PENDING" ]]; then
  fail "the certificate is no longer the placeholder: the block must give its keyring SHA-256 $actual instead of $PENDING"
else
  fail "the block expects the key SHA-256 $expected, the keyring made from installers/apt/orkeon-archive-keyring.asc has $actual"
fi

if [[ "$errors" -gt 0 ]]; then
  echo "check-apt-snippet: $errors problem(s)" >&2
  exit 1
fi
echo "check-apt-snippet passed: one block, identical in both languages, its source and its key SHA-256 match installers/apt."
