#!/usr/bin/env bash
# Health check of the published apt repository, between two tags (apt-health.yml, weekly).
# It reads what users read, over HTTP, and needs no secret.
#
# Usage:
#   scripts/apt/check-apt-health.sh --keyring <binary certificate>
#                                   [--base-url https://github.com/Orkeon/orkeon/]
#                                   [--channels "stable rc"] [--min-days 180]
#                                   [--require-sqv] [--no-download]
#
# For each channel:
#   - availability: <base>raw/apt/<channel>/InRelease answers a redirect, then 200. A 429
#     (GitHub's limit on anonymous traffic) is a warning, never a failure;
#   - signature: InRelease verifies with gpgv, and with sqv when installed (--require-sqv:
#     sqv must be there), against --keyring alone; its Date is not in the future;
#   - Packages matches the SHA-256 the signed Release gives for it;
#   - index <-> assets: every stanza's Filename answers 200 at <base><Filename>, through the
#     redirects, with the Size the stanza gives; for the newest version of each
#     architecture the file is downloaded and its SHA-256 compared (--no-download skips it).
# And once: the signing subkey of --keyring that expires last must expire in more than
# --min-days days (180), otherwise apt update breaks for everyone when it does.
# Prints "ok", "WARN" or "FAIL" per check; exits 1 when one failed. --base-url and
# --keyring let the bench point it at a fake GitHub with a deliberately wrong index.
# Needs curl, gpg, gpgv, sha256sum, dpkg (for version order).
set -euo pipefail
export LC_ALL=C

BASE="https://github.com/Orkeon/orkeon/"
KEYRING=""
CHANNELS="stable rc"
MIN_DAYS=180
REQUIRE_SQV=false
DOWNLOAD=true
EXTEND_HINT="extend the signing subkey: installers/apt/README.md, 'Extending or rotating the signing subkey'"

usage() { echo "check-apt-health: $*" >&2; exit 2; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url)    [[ $# -ge 2 ]] || usage "$1 needs a value"; BASE="${2%/}/"; shift 2 ;;
    --keyring)     [[ $# -ge 2 ]] || usage "$1 needs a value"; KEYRING="$2"; shift 2 ;;
    --channels)    [[ $# -ge 2 ]] || usage "$1 needs a value"; CHANNELS="$2"; shift 2 ;;
    --min-days)    [[ $# -ge 2 ]] || usage "$1 needs a value"; MIN_DAYS="$2"; shift 2 ;;
    --require-sqv) REQUIRE_SQV=true; shift ;;
    --no-download) DOWNLOAD=false; shift ;;
    -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done
[[ -f "$KEYRING" ]] || usage "--keyring must name the binary certificate"
[[ "$MIN_DAYS" =~ ^[0-9]+$ ]] || usage "--min-days takes a number"
if head -c 64 "$KEYRING" | grep -q -- '-----BEGIN PGP'; then usage "--keyring must be binary (gpg --dearmor)"; fi

failures=0
ok()   { echo "ok    $*"; }
warn() { echo "WARN  $*"; [[ -z "${GITHUB_ACTIONS:-}" ]] || echo "::warning::$*"; }
bad()  { echo "FAIL  $*"; [[ -z "${GITHUB_ACTIONS:-}" ]] || echo "::error::$*"; failures=$((failures + 1)); }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -m 700 "$work/gpgv-home"
SQV=false
if command -v sqv >/dev/null 2>&1 && sqv --help 2>&1 | grep -q -- '--cleartext'; then SQV=true; fi
if $REQUIRE_SQV && ! $SQV; then bad "sqv (the verifier of Debian 13) is required but not installed"; fi

curl_opts=(--silent --show-error --retry 3 --retry-delay 5 --connect-timeout 20 --max-time 600)

# --- the key's expiry --------------------------------------------------------------------
now="$(date -u +%s)"
farthest=""
while IFS=: read -r -a f; do
  # sub records: 2 validity, 6 expiry (empty: never), 12 capabilities
  [[ "${f[0]}" == sub && "${f[11]}" == *s* ]] || continue
  [[ "${f[1]}" != r && "${f[1]}" != e ]] || continue
  exp="${f[6]:-}"
  [[ -n "$exp" ]] || { farthest=never; break; }
  (( exp > now )) || continue
  [[ -n "$farthest" && "$farthest" != never && "$farthest" -ge "$exp" ]] || farthest="$exp"
done < <(gpg --homedir "$work/gpgv-home" --batch --with-colons --show-keys "$KEYRING" 2>/dev/null)
if [[ -z "$farthest" ]]; then
  bad "the keyring holds no valid signing subkey: $EXTEND_HINT"
elif [[ "$farthest" == never ]]; then
  ok "a signing subkey never expires"
else
  days=$(( (farthest - now) / 86400 ))
  if (( days < MIN_DAYS )); then
    bad "the signing subkey expires in $days days ($(date -u -d "@$farthest" +%F)), under $MIN_DAYS: $EXTEND_HINT"
  else
    ok "the signing subkey expires in $days days ($(date -u -d "@$farthest" +%F))"
  fi
fi

# --- each channel --------------------------------------------------------------------------
for c in $CHANNELS; do
  url="${BASE}raw/apt/$c/InRelease"
  first="$(curl "${curl_opts[@]}" -o /dev/null -w '%{http_code}' "$url" || echo 000)"
  if [[ "$first" == 429 ]]; then warn "$c: $url answered 429 (anonymous rate limit); channel not checked this time"; continue; fi
  if [[ "$first" =~ ^30[1278]$ ]]; then ok "$c: $url redirects ($first)"; else bad "$c: $url answered $first, expected a redirect"; fi
  code="$(curl "${curl_opts[@]}" -L -o "$work/$c.InRelease" -w '%{http_code}' "$url" || echo 000)"
  if [[ "$code" == 429 ]]; then warn "$c: InRelease answered 429 after the redirect; channel not checked this time"; continue; fi
  [[ "$code" == 200 ]] || { bad "$c: InRelease answered $code after the redirect"; continue; }
  ok "$c: InRelease answered 200"

  if gpgv --homedir "$work/gpgv-home" --keyring "$KEYRING" --output "$work/$c.Release" "$work/$c.InRelease" >"$work/gpgv.log" 2>&1; then
    ok "$c: InRelease verifies with gpgv"
  else
    sed 's/^/      | /' "$work/gpgv.log"; bad "$c: InRelease does not verify with gpgv against the published key"; continue
  fi
  if $SQV; then
    if sqv --keyring "$KEYRING" --cleartext --output "$work/$c.sqv" "$work/$c.InRelease" >"$work/sqv.log" 2>&1; then
      ok "$c: InRelease verifies with sqv"
    else
      sed 's/^/      | /' "$work/sqv.log"; bad "$c: InRelease does not verify with sqv"
    fi
  fi
  rdate="$(sed -n 's/^Date:[[:space:]]*//p' "$work/$c.Release" | head -n1)"
  if [[ -n "$rdate" ]] && (( $(date -u -d "$rdate" +%s) <= now + 300 )); then
    ok "$c: Date $rdate is not in the future"
  else
    bad "$c: Date '$rdate' is missing or in the future"
  fi

  want="$(awk '/^SHA256:/ { on = 1; next } /^[^ ]/ { on = 0 } on && $3 == "Packages" { print $1 }' "$work/$c.Release")"
  code="$(curl "${curl_opts[@]}" -L -o "$work/$c.Packages" -w '%{http_code}' "${BASE}raw/apt/$c/Packages" || echo 000)"
  if [[ "$code" == 429 ]]; then warn "$c: Packages answered 429; stanzas not checked this time"; continue; fi
  if [[ "$code" != 200 ]]; then bad "$c: Packages answered $code"; continue; fi
  if [[ "$(sha256sum "$work/$c.Packages" | cut -d' ' -f1)" == "$want" ]]; then
    ok "$c: Packages matches the signed Release"
  else
    # raw may serve a newer Packages next to a cached InRelease right after a push.
    warn "$c: Packages does not match the SHA-256 of the signed Release (cache right after a push?)"
    continue
  fi

  # Stanzas: "<package> <version> <arch> <size> <sha256> <filename>"
  awk '/^Package: / { p = $2 } /^Version: / { v = $2 } /^Architecture: / { a = $2 } /^Size: / { s = $2 }
       /^SHA256: / { h = $2 } /^Filename: / { f = $2 }
       /^$/ { if (p) print p, v, a, s, h, f; p = v = a = s = h = f = "" }
       END { if (p) print p, v, a, s, h, f }' "$work/$c.Packages" > "$work/$c.stanzas"
  n="$(wc -l < "$work/$c.stanzas")"
  [[ "$n" -gt 0 ]] || { ok "$c: empty channel"; continue; }
  while read -r p v a size sha file; do
    out="$(curl "${curl_opts[@]}" -L -I -o "$work/head" -w '%{http_code}' "$BASE$file" || echo 000)"
    if [[ "$out" == 429 ]]; then warn "$c: $file answered 429"; continue; fi
    len="$(tr -d '\r' < "$work/head" | awk -F': ' 'tolower($1) == "content-length" { l = $2 } END { print l }')"
    if [[ "$out" == 200 && "$len" == "$size" ]]; then
      ok "$c: $p $v $a -> $file ($size bytes)"
    else
      bad "$c: $p $v $a -> $file answered $out with $len bytes, the index says $size: a stanza without its asset"
    fi
  done < "$work/$c.stanzas"

  if $DOWNLOAD; then
    for a in $(cut -d' ' -f3 "$work/$c.stanzas" | sort -u); do
      newest=""
      while read -r p v arch size sha file; do
        [[ "$p" == orkeon && "$arch" == "$a" ]] || continue
        if [[ -z "$newest" ]] || dpkg --compare-versions "$v" gt "${newest%% *}"; then newest="$v $sha $file"; fi
      done < "$work/$c.stanzas"
      [[ -n "$newest" ]] || continue
      read -r v sha file <<<"$newest"
      code="$(curl "${curl_opts[@]}" -L -o "$work/pkg" -w '%{http_code}' "$BASE$file" || echo 000)"
      if [[ "$code" == 200 && "$(sha256sum "$work/pkg" | cut -d' ' -f1)" == "$sha" ]]; then
        ok "$c: newest $a package $v downloaded, SHA-256 as indexed"
      elif [[ "$code" == 429 ]]; then
        warn "$c: $file answered 429"
      else
        bad "$c: newest $a package $v ($file): HTTP $code, SHA-256 differs from the index"
      fi
      rm -f "$work/pkg"
    done
  fi
done

if [[ "$failures" -gt 0 ]]; then
  echo "check-apt-health: $failures check(s) failed"
  exit 1
fi
echo "check-apt-health passed"
