#!/usr/bin/env bash
# Guard of the apt repository's signing certificate, installers/apt/orkeon-archive-keyring.asc.
# Offline, no secret: it reads the public certificate only.
#
# Usage:
#   scripts/apt/check-signing-key.sh [--cert FILE] [--docs-root DIR] [--require-real]
#                                    [--min-days N]
#
# It fails when:
#   - the certificate is not exactly one OpenPGP v4 ed25519 primary key, certification only
#     ([C]), without expiry, carrying the user ID "Orkeon Archive Signing Key <arion@orkeon.org>";
#   - no valid (neither expired nor revoked) ed25519 signing subkey [S] is present, or the
#     farthest signing expiry is less than --min-days (180) days away: a subkey that expires
#     breaks `apt update` for every user, so it must be extended long before;
#   - a document cites another fingerprint or another keyring SHA-256 than the certificate's.
#
# Document markers. Each of the files below is searched for these two lines (anywhere in a
# line, typically in a code block; spaces inside the values and their case do not matter):
#     orkeon-archive-keyring fingerprint: <40 hex digits, the primary key fingerprint>
#     orkeon-archive-keyring.gpg sha256: <64 hex digits, sha256 of `gpg --dearmor` output>
# Files: SECURITY.md, SECURITY.fr.md, docs/guides/install-with-apt.md,
# docs/fr/guides/install-with-apt.md. A missing file is skipped. Every marker present must
# match. SECURITY.md and SECURITY.fr.md must exist and carry both markers, except while the
# certificate is the development placeholder (an armor header line
# "Comment: PLACEHOLDER ..."), when absent markers only print a notice.
#
# --require-real fails on the placeholder certificate: a release that publishes an apt
# index must never sign with, nor ship, the throwaway key.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CERT="$REPO_ROOT/installers/apt/orkeon-archive-keyring.asc"
DOCS_ROOT="$REPO_ROOT"
REQUIRE_REAL=false
MIN_DAYS=180
EXPECTED_UID="Orkeon Archive Signing Key <arion@orkeon.org>"
DOC_FILES="SECURITY.md SECURITY.fr.md docs/guides/install-with-apt.md docs/fr/guides/install-with-apt.md"
REQUIRED_DOC_FILES="SECURITY.md SECURITY.fr.md"
FPR_MARKER="orkeon-archive-keyring fingerprint:"
SUM_MARKER="orkeon-archive-keyring.gpg sha256:"
EXTEND_HINT="Extend the signing subkey (installers/apt/README.md, 'Extending or rotating the signing subkey'), then bump installers/apt/keyring.version."

while [[ $# -gt 0 ]]; do
  case "$1" in
    --cert)         CERT="$2"; shift 2 ;;
    --docs-root)    DOCS_ROOT="$2"; shift 2 ;;
    --require-real) REQUIRE_REAL=true; shift ;;
    --min-days)     MIN_DAYS="$2"; shift 2 ;;
    -h|--help) sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v gpg >/dev/null 2>&1 || { echo "gpg not found (install the 'gnupg' package)." >&2; exit 1; }
[[ -f "$CERT" ]] || { echo "Certificate not found: $CERT" >&2; exit 1; }

errors=0
fail() { echo "error: $*" >&2; errors=$((errors + 1)); }

# An empty, private GNUPGHOME: the guard must never read nor write the caller's keyrings.
GNUPGHOME="$(mktemp -d)"
export GNUPGHOME
chmod 700 "$GNUPGHOME"
: > "$GNUPGHOME/empty.md"
trap 'gpgconf --kill all >/dev/null 2>&1 || true; rm -rf "$GNUPGHOME"' EXIT

placeholder=false
if grep -q '^Comment: PLACEHOLDER' "$CERT"; then placeholder=true; fi

colons="$(gpg --batch --show-keys --with-colons "$CERT" 2>/dev/null)" \
  || { echo "error: gpg cannot read $CERT" >&2; exit 1; }

# --- 1. The primary key ------------------------------------------------------------
pub_count="$(grep -c '^pub:' <<<"$colons" || true)"
[[ "$pub_count" -eq 1 ]] || fail "expected exactly one primary key, found $pub_count"

# Fields (gpg doc/DETAILS): 2 validity, 4 algorithm (22 = EdDSA), 7 expiry,
# 12 capabilities (lower case: this key's own), 17 curve. The fpr record after the key
# gives its fingerprint: 40 hex digits is a v4 key (v5 and v6 have 64).
pub_line="$(grep -m1 '^pub:' <<<"$colons" || true)"
FPR="$(awk -F: '/^pub:/ {getline; if ($1 == "fpr") print $10; exit}' <<<"$colons")"
IFS=: read -r -a pub <<<"$pub_line"
[[ "${pub[3]:-}" == 22 && "${pub[16]:-}" == ed25519 ]] || fail "primary key is not ed25519 (algorithm ${pub[3]:-?}, curve ${pub[16]:-?})"
[[ "${#FPR}" -eq 40 ]] || fail "primary key is not an OpenPGP v4 key (fingerprint '$FPR'): apt's gpgv reads no v6 key and Sequoia no v5"
own_caps="$(tr -cd 'a-z' <<<"${pub[11]:-}")"
[[ "$own_caps" == c ]] || fail "primary key capabilities are '${own_caps}', expected certification only (c)"
[[ -z "${pub[6]:-}" ]] || fail "primary key expires ($(date -u -d "@${pub[6]}" +%F)); it must not"
[[ "${pub[1]:-}" != r ]] || fail "primary key is revoked"
if ! gpg --batch --list-packets "$CERT" 2>/dev/null | grep -A1 '^:public key packet:' | grep -q 'version 4,'; then
  fail "the public key packet is not version 4"
fi

uids="$(awk -F: '$1 == "uid" && $2 != "r" {print $10}' <<<"$colons")"
[[ "$uids" == "$EXPECTED_UID" ]] || fail "user IDs are '$(tr '\n' ';' <<<"$uids")', expected exactly '$EXPECTED_UID'"

# --- 2. The signing subkeys --------------------------------------------------------
now="$(date -u +%s)"
farthest=""   # empty: no valid signing subkey; "never": one does not expire
while IFS=: read -r -a sub; do
  [[ "${sub[0]}" == sub ]] || continue
  [[ "${sub[11]}" == *s* ]] || continue
  [[ "${sub[1]}" != r && "${sub[1]}" != e && "${sub[1]}" != i ]] || continue
  [[ "${sub[3]}" == 22 && "${sub[16]}" == ed25519 ]] || continue
  if [[ -z "${sub[6]}" ]]; then farthest=never; continue; fi
  (( sub[6] > now )) || continue
  if [[ "$farthest" != never ]] && [[ -z "$farthest" || "${sub[6]}" -gt "$farthest" ]]; then farthest="${sub[6]}"; fi
done <<<"$colons"

if [[ -z "$farthest" ]]; then
  fail "no valid ed25519 signing subkey [S]. $EXTEND_HINT"
elif [[ "$farthest" != never ]]; then
  days_left=$(( (farthest - now) / 86400 ))
  if (( days_left < MIN_DAYS )); then
    fail "the signing subkey expires on $(date -u -d "@$farthest" +%F), in $days_left days (minimum $MIN_DAYS). $EXTEND_HINT"
  fi
fi

# --- 3. The documents ----------------------------------------------------------------
KEYRING_SHA="$(gpg --batch --dearmor < "$CERT" | sha256sum | cut -d' ' -f1)"

# Prints the normalised value of every <marker> line of <file>, one per line.
marker_values() { # <file> <marker>
  awk -v m="$(tr 'A-Z' 'a-z' <<<"$2")" '{
    # A marker line starts with the marker (indentation allowed): prose that quotes the
    # marker mid-sentence, or names it in backticks, is not a marker line.
    l = $0; sub(/^[ \t]*/, "", l)
    if (index(tolower(l), m) != 1) next
    r = substr(l, length(m) + 1)
    sub(/^[ \t]*/, "", r)
    match(r, /^[0-9A-Fa-f ]*/); r = substr(r, 1, RLENGTH)
    gsub(/ /, "", r); print toupper(r)
  }' "$1"
}

check_marker() { # <file> <marker> <expected, upper-case hex> <label> <required>
  local file="$1" marker="$2" expected="$3" label="$4" required="$5" v found=false doc
  doc="$DOCS_ROOT/$file"; [[ -f "$doc" ]] || doc="$GNUPGHOME/empty.md"
  while IFS= read -r v; do
    [[ -n "$v" ]] || continue
    found=true
    [[ "$v" == "$expected" ]] || fail "$file cites $label $v; the certificate's is $expected"
  done < <(marker_values "$doc" "$marker")
  if [[ "$found" == false && "$required" == true ]]; then
    if [[ "$placeholder" == true ]]; then
      echo "notice: $file has no '$marker' line; accepted while the certificate is the placeholder."
    else
      fail "$file has no '$marker' line (expected $expected)"
    fi
  fi
}

for f in $DOC_FILES; do
  required=false
  [[ " $REQUIRED_DOC_FILES " == *" $f "* ]] && required=true
  # A required file that is missing reads as one without marker.
  [[ -f "$DOCS_ROOT/$f" || "$required" == true ]] || continue
  check_marker "$f" "$FPR_MARKER" "$FPR" "fingerprint" "$required"
  check_marker "$f" "$SUM_MARKER" "${KEYRING_SHA^^}" "keyring SHA-256" "$required"
done

if [[ "$placeholder" == true ]]; then
  if [[ "$REQUIRE_REAL" == true ]]; then
    fail "$CERT is the development placeholder (a throwaway key); replace it with the key ceremony output (installers/apt/README.md)"
  else
    echo "notice: $CERT is the development placeholder, to be replaced by the key ceremony output."
  fi
fi

if (( errors > 0 )); then
  echo "check-signing-key: $errors error(s) in $CERT" >&2
  exit 1
fi
expiry="never"; [[ "$farthest" != never ]] && expiry="$(date -u -d "@$farthest" +%F)"
echo "check-signing-key: ok - $FPR, signing until $expiry, keyring sha256 $KEYRING_SHA"
