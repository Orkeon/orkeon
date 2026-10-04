#!/usr/bin/env bash
# Regression test of scripts/apt/check-signing-key.sh. Throwaway ed25519 keys are generated
# here, in a temporary GNUPGHOME destroyed at exit: no network, no secret, and never the
# caller's keyrings. Each case builds a certificate and fake SECURITY files, runs the guard,
# and checks its exit code and its message. Run: bash scripts/apt/test-check-signing-key.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
guard="$here/check-signing-key.sh"
work="$(mktemp -d)"
# One GNUPGHOME per key (several share the user ID): $work/gnupg-<name>.
cleanup() {
  local h
  for h in "$work"/gnupg-*; do [[ -d "$h" ]] && GNUPGHOME="$h" gpgconf --kill all >/dev/null 2>&1; done
  rm -rf "$work"
}
trap cleanup EXIT

UID_OK="Orkeon Archive Signing Key <arion@orkeon.org>"
# k <name> <gpg arguments...>: gpg on the key <name>'s own home, quiet, empty passphrase.
k() {
  local home="$work/gnupg-$1"; shift
  [[ -d "$home" ]] || { mkdir -m 700 "$home"; echo 'trust-model always' > "$home/gpg.conf"; }
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase '' "$@" 2>/dev/null
}
fpr_of() { k "$1" --list-keys --with-colons | awk -F: '/^fpr:/ {print $10; exit}'; }
export_key() { k "$1" --armor --export > "$work/$1.asc"; }

# new_key <name> <uid> <primary usage> [<subkey expiry>...]: generates a key, exports its
# public certificate to $work/<name>.asc and prints its fingerprint.
new_key() {
  local name="$1" uid="$2" usage="$3" fpr exp; shift 3
  k "$name" --quick-generate-key "$uid" ed25519 "$usage" never
  fpr="$(fpr_of "$name")"
  for exp in "$@"; do k "$name" --quick-add-key "$fpr" ed25519 sign "$exp"; done
  export_key "$name"
  echo "$fpr"
}
keyring_sha() { k sha --dearmor < "$1" | sha256sum | cut -d' ' -f1; }

# docs <dir> <fingerprint> <sha256>: SECURITY.md and SECURITY.fr.md with both markers,
# the fingerprint written in groups of four, as gpg --fingerprint prints it.
docs() {
  mkdir -p "$1"
  local spaced; spaced="$(sed -E 's/(.{4})/\1 /g; s/ $//' <<<"$2")"
  for f in SECURITY.md SECURITY.fr.md; do
    printf '# Security\n\n```text\norkeon-archive-keyring fingerprint: %s\norkeon-archive-keyring.gpg sha256: %s\n```\n' \
      "$spaced" "$3" > "$1/$f"
  done
}

failed=0
run() { # <cert> <docs dir> [guard options...]
  set +e; out="$(bash "$guard" --cert "$1" --docs-root "$2" "${@:3}" 2>&1)"; code=$?; set -e
}
check() { # <description> <expected exit: 0 or 1> [<fragment of the output>]
  if [[ "$code" -eq "$2" ]] && { [[ -z "${3:-}" ]] || grep -qF -- "$3" <<<"$out"; }; then
    echo "ok    $1"
  else
    echo "FAIL  $1 (exit $code)"; sed 's/^/      /' <<<"$out"; failed=$((failed + 1))
  fi
}

# Five keys: the good one (2 years), one whose only signing subkey ends in 30 days, one
# with a 30-day and a 2-year subkey, one with no signing subkey, one with a wrong user ID.
fpr_good="$(new_key good "$UID_OK" cert 2y)"
sha_good="$(keyring_sha "$work/good.asc")"
docs "$work/docs-good" "$fpr_good" "$sha_good"

run "$work/good.asc" "$work/docs-good"
check "a 2-year signing subkey with matching SECURITY files passes" 0 "check-signing-key: ok - $fpr_good"

run "$work/good.asc" "$work/docs-good" --require-real
check "--require-real accepts a certificate that is not the placeholder" 0

fpr_short="$(new_key short "$UID_OK" cert 30d)"
docs "$work/docs-short" "$fpr_short" "$(keyring_sha "$work/short.asc")"
run "$work/short.asc" "$work/docs-short"
check "a signing subkey expiring in 30 days fails, pointing to the extension procedure" 1 "(minimum 180). Extend the signing subkey"
[[ "$(grep -c '^error:' <<<"$out")" -eq 1 ]] && echo "ok    ... and for that reason only" \
  || { echo "FAIL  the 30-day case fails for another reason too"; failed=$((failed + 1)); }

k short --quick-add-key "$fpr_short" ed25519 sign 2y
export_key short
docs "$work/docs-short" "$fpr_short" "$(keyring_sha "$work/short.asc")"
run "$work/short.asc" "$work/docs-short"
check "the farthest signing expiry counts: a 30-day and a 2-year subkey pass" 0

new_key nosign "$UID_OK" cert >/dev/null
run "$work/nosign.asc" "$work/docs-good"
check "a certificate without signing subkey fails" 1 "no valid ed25519 signing subkey"

new_key both "$UID_OK" cert,sign 2y >/dev/null
run "$work/both.asc" "$work/docs-good"
check "a primary key that also signs fails" 1 "expected certification only"

new_key baduid "Somebody <someone@example.org>" cert 2y >/dev/null
run "$work/baduid.asc" "$work/docs-good"
check "a wrong user ID fails" 1 "expected exactly '$UID_OK'"

fpr_other="$(new_key other "$UID_OK" cert 2y)"
docs "$work/docs-other-fpr" "$fpr_other" "$sha_good"
run "$work/good.asc" "$work/docs-other-fpr"
check "another fingerprint in SECURITY.md fails" 1 "SECURITY.md cites fingerprint $fpr_other"

docs "$work/docs-other-sha" "$fpr_good" "$(keyring_sha "$work/other.asc")"
run "$work/good.asc" "$work/docs-other-sha"
check "another keyring SHA-256 in SECURITY.md fails" 1 "SECURITY.md cites keyring SHA-256"

cp -R "$work/docs-good" "$work/docs-apt-page"
mkdir -p "$work/docs-apt-page/docs/fr/guides"
echo "orkeon-archive-keyring fingerprint: $fpr_other" > "$work/docs-apt-page/docs/fr/guides/install-with-apt.md"
run "$work/good.asc" "$work/docs-apt-page"
check "an apt page that cites another fingerprint fails" 1 "docs/fr/guides/install-with-apt.md cites fingerprint"

mkdir -p "$work/docs-none"
run "$work/good.asc" "$work/docs-none"
check "SECURITY files without markers fail once the key is real" 1 "has no 'orkeon-archive-keyring fingerprint:' line"

# The placeholder: the same certificate under the armor header the committed one carries.
sed '1a Comment: PLACEHOLDER - throwaway key' "$work/good.asc" > "$work/placeholder.asc"
run "$work/placeholder.asc" "$work/docs-none"
check "the placeholder without markers passes with a notice" 0 "accepted while the certificate is the placeholder"
run "$work/placeholder.asc" "$work/docs-other-fpr"
check "the placeholder is still compared with the markers present" 1 "cites fingerprint"
run "$work/placeholder.asc" "$work/docs-none" --require-real
check "--require-real refuses the placeholder" 1 "is the development placeholder"

if (( failed > 0 )); then echo "$failed case(s) failed"; exit 1; fi
echo "all cases passed"
