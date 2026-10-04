#!/usr/bin/env bash
# End-to-end bench of the apt publication chain, as release.yml and apt-maintenance.yml run
# it, against a local bare repository and the fake GitHub. Needs docker and python3; no
# GitHub, no real key, no secret.
#
# Usage: scripts/apt/test-apt-publish.sh
#
# A debian:13 "builder" container (apt-utils, gpg, sqv) makes a throwaway key shaped like the
# real one (v4, ed25519 certification-only primary, ed25519 signing subkey valid 2 years,
# user ID "Orkeon Archive Signing Key <arion@orkeon.org>") and packages laid out like the
# real orkeon .deb (CLI and the two Studio launchers answering --version, md5sums,
# Recommends: orkeon-archive-keyring) for v1.0.0-rc.4 and v1.0.0-rc.5, amd64 and arm64.
# It then publishes, exactly as the workflows do, through publish-apt-channel.sh and
# update-apt-channels.sh into a bare repository: the first tag bootstraps the orphan branch
# (rc and an empty stable), the second one adds to rc and re-attaches the published keyring
# package (stage-keyring-deb.sh), then a yank and a resign. Then check-apt-branch.sh
# replays the documented installation block (verify-apt-repo.sh, a copy of the page whose
# key SHA-256 is the throwaway key's) in a debian:13 container against the fake GitHub:
# install, upgrade rc.4 -> rc.5, removal; and check-apt-health.sh passes on that state and
# fails on the three faults it exists for: a stanza without its asset, an InRelease that
# does not verify, a signing subkey that expires within 180 days.
# The containers reach the distribution's mirrors (sudo, curl, ca-certificates).
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$here/../.." && pwd)"
BUILDER_IMAGE="debian:13"
CLIENT_IMAGE="${APT_TEST_CLIENT_IMAGE:-debian:13}"

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}

# --- Inside the builder ------------------------------------------------------------------
PASSPHRASE="throwaway publish bench passphrase"

make_key() { # <dir> <name> <subkey validity>
  local dir="$1" name="$2" validity="$3" home fpr
  home="$(mktemp -d)"
  chmod 700 "$home"
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase "$PASSPHRASE" \
    --quick-gen-key "Orkeon Archive Signing Key <arion@orkeon.org>" ed25519 cert never 2>/dev/null
  fpr="$(GNUPGHOME="$home" gpg --batch --with-colons --list-keys 2>/dev/null | awk -F: '$1 == "fpr" { print $10; exit }')"
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase "$PASSPHRASE" \
    --quick-add-key "$fpr" ed25519 sign "$validity" 2>/dev/null
  GNUPGHOME="$home" gpg --batch --quiet --export "$fpr" > "$dir/$name.gpg"
  GNUPGHOME="$home" gpg --batch --quiet --armor --export "$fpr" > "$dir/$name.asc"
  GNUPGHOME="$home" gpg --batch --quiet --pinentry-mode loopback --passphrase "$PASSPHRASE" \
    --armor --export-secret-subkeys "$fpr" > "$dir/$name-secret.asc"
  GNUPGHOME="$home" gpgconf --kill all >/dev/null 2>&1 || true
  rm -rf "$home"
}

make_orkeon_deb() { # <out dir> <upstream version> <arch>
  local out="$1" up="$2" arch="$3" ver root app
  ver="${up//-/\~}"
  root="$(mktemp -d)"
  mkdir -p "$root/DEBIAN" "$root/usr/bin" "$root/usr/lib/orkeon" "$root/usr/share/doc/orkeon"
  printf '#!/bin/sh\n[ "$1" = --version ] && { echo "orkeon %s"; exit 0; }\necho "bench orkeon"\n' "$up" > "$root/usr/lib/orkeon/orkeon"
  ln -s ../lib/orkeon/orkeon "$root/usr/bin/orkeon"
  for app in orkeon-studio-config orkeon-studio-run; do
    mkdir -p "$root/usr/lib/$app"
    printf '#!/bin/sh\n[ "$1" = --version ] && { echo "%s %s"; exit 0; }\nexit 1\n' "$app" "$up" > "$root/usr/lib/$app/$app"
    ln -s "../lib/$app/$app" "$root/usr/bin/$app"
  done
  echo "bench package" > "$root/usr/share/doc/orkeon/README"
  chmod 0755 "$root"/usr/lib/*/orkeon*
  cat > "$root/DEBIAN/control" <<EOF
Package: orkeon
Version: $ver
Architecture: $arch
Maintainer: Orkeon Contributors <arion@orkeon.org>
Recommends: orkeon-archive-keyring
Section: devel
Priority: optional
Description: Orkeon bench package (not a real build)
 Laid out like the real package, to exercise the apt publication chain.
EOF
  (cd "$root" && find usr -type f -print0 | sort -z | xargs -0 md5sum) > "$root/DEBIAN/md5sums"
  find "$root" -exec touch -h -d @1700000000 {} +
  SOURCE_DATE_EPOCH=1700000000 dpkg-deb --root-owner-group -Zgzip --build "$root" "$out/orkeon_${up}_${arch}.deb" >/dev/null
  rm -rf "$root"
}

# release <tag>: the Release assets of a tag, and the debs.list fetch-release-debs.sh writes
release() {
  local tag="$1" up="${1#v}" a
  mkdir -p "/work/rel/$tag"
  for a in amd64 arm64; do make_orkeon_deb "/work/rel/$tag" "$up" "$a"; done
}
list_of() { # <tag>
  local f
  for f in "/work/rel/$1"/*.deb; do echo "$f=$1/$(basename "$f")"; done > "/work/debs-$1.list"
}

# publish <publish-index.sh arguments...>: as the workflows run it, from the checkout
publish() {
  APT_SIGNING_KEY="$(cat /work/key/archive-secret.asc)" APT_SIGNING_PASSPHRASE="$PASSPHRASE" \
  GIT_AUTHOR_NAME='' GIT_AUTHOR_EMAIL='' GIT_COMMITTER_NAME='' GIT_COMMITTER_EMAIL='' \
    bash /repo/scripts/apt/publish-index.sh --repo-dir /work/checkout --keyring /work/key/archive.gpg "$@"
}

builder() {
  mkdir -p /work/key /work/rel
  make_key /work/key archive 2y
  # The repository's certificate becomes the throwaway one: the keyring package and the
  # index are made from it, as release.yml makes them from the real one.
  cp /work/key/archive.asc /repo/installers/apt/orkeon-archive-keyring.asc
  echo 2026.10.02 > /repo/installers/apt/keyring.version
  git init -q --bare /work/remote.git
  # The checkout the workflow runs from: origin is the bare repository.
  git clone -q /work/remote.git /work/checkout 2>/dev/null
  git -C /work/checkout -c user.name=bench -c user.email=bench@orkeon.invalid commit -q --allow-empty -m main
  git -C /work/checkout push -q origin HEAD:refs/heads/main

  echo "# tag v1.0.0-rc.4: the first publication bootstraps the branch"
  release v1.0.0-rc.4
  bash /repo/scripts/package-keyring-deb.sh --out /work/rel/v1.0.0-rc.4 >/dev/null
  rm -rf /work/rel/v1.0.0-rc.4/_keyring-stage /work/rel/v1.0.0-rc.4/SHA256SUMS
  list_of v1.0.0-rc.4
  publish --message v1.0.0-rc.4 --channel rc --ensure stable --debs /work/debs-v1.0.0-rc.4.list
  sleep 1

  echo "# tag v1.0.0-rc.5: rc only, the keyring package attached again"
  release v1.0.0-rc.5
  bash /repo/scripts/apt/stage-keyring-deb.sh --out /work/rel/v1.0.0-rc.5 \
    --published /work/rel/v1.0.0-rc.4/orkeon-archive-keyring_2026.10.02_all.deb >/dev/null
  rm -f /work/rel/v1.0.0-rc.5/SHA256SUMS
  list_of v1.0.0-rc.5
  publish --message v1.0.0-rc.5 --channel rc --ensure stable --debs /work/debs-v1.0.0-rc.5.list
  sleep 1

  git clone -q --branch apt /work/remote.git /work/branch
  log="$(git -C /work/branch log --format=%s)"
  echo "$log" | sed 's/^/      | /'
  check "two commits, one per tag" test "$(wc -l <<<"$log")" -eq 2
  check "first: rc and the empty stable" test "$(sed -n 2p <<<"$log")" = "apt: rc,stable v1.0.0-rc.4"
  check "second: rc only" test "$(sed -n 1p <<<"$log")" = "apt: rc v1.0.0-rc.5"
  check "the author is github-actions[bot]" test "$(git -C /work/branch log -1 --format=%an)" = "github-actions[bot]"
  check "the root keyring is the certificate, binary" cmp -s /work/branch/orkeon-archive-keyring.gpg /work/key/archive.gpg
  check "stable is a valid empty channel" bash -c 'gpgv --keyring /work/key/archive.gpg /work/branch/stable/InRelease 2>/dev/null && [ ! -s /work/branch/stable/Packages ]'
  check "rc lists rc.4 and rc.5 on both architectures, and the keyring once" test \
    "$(awk '/^Package:/{p=$2}/^Version:/{v=$2}/^Architecture:/{a=$2}/^$/{print p, v, a}' /work/branch/rc/Packages | tr '\n' ,)" = \
    "orkeon 1.0.0~rc.4 amd64,orkeon 1.0.0~rc.4 arm64,orkeon 1.0.0~rc.5 amd64,orkeon 1.0.0~rc.5 arm64,orkeon-archive-keyring 2026.10.02 all,"
  check "the keyring keeps its first Filename" grep -qx 'Filename: releases/download/v1.0.0-rc.4/orkeon-archive-keyring_2026.10.02_all.deb' /work/branch/rc/Packages

  echo "# maintenance: yank, then resign"
  out="$(publish --message 'yank orkeon=1.0.0~rc.4/arm64' --channel stable --channel rc --yank 'orkeon=1.0.0~rc.4/arm64' 2>&1)"
  check "a yank touches only the channel that lists it" bash -c 'git -C /work/branch pull -q && [ "$(git -C /work/branch log -1 --format=%s)" = "apt: rc yank orkeon=1.0.0~rc.4/arm64" ]'
  check "... and removes the stanza" bash -c '! grep -A2 "^Version: 1.0.0~rc.4$" /work/branch/rc/Packages | grep -q "^Architecture: arm64"'
  sleep 1
  out="$(publish --message 'yank orkeon=9.9.9' --channel stable --channel rc --yank 'orkeon=9.9.9' 2>&1)" && code=0 || code=$?
  check "a yank that matches nothing fails" test "$code" -ne 0
  before="$(sed -n 's/^Date: //p' /work/branch/rc/Release)"
  sleep 1
  publish --message resign --channel stable --channel rc >/dev/null 2>&1
  git -C /work/branch pull -q
  check "resign signs both channels again" test "$(git -C /work/branch log -1 --format=%s)" = "apt: stable,rc resign"
  check "... with a later Date" bash -c '[ "$(date -u -d "$(sed -n "s/^Date: //p" /work/branch/rc/Release)" +%s)" -gt "$(date -u -d "$0" +%s)" ]' "$before"
  check "... that verifies" gpgv --keyring /work/key/archive.gpg /work/branch/rc/InRelease

  if [ "$failed" -gt 0 ]; then echo "builder: ${failed} check(s) failed"; exit 1; fi
  echo "builder passed"
}

# health: check-apt-health.sh against the fake GitHub, on the state the builder published
health() {
  echo "127.0.0.1 github.test raw.test objects.test" >> /etc/hosts
  local url=http://github.test:8081/Orkeon/orkeon/
  serve() { # <branch dir>
    [ ! -f /work/fg.pid ] || kill "$(cat /work/fg.pid)" 2>/dev/null || true
    python3 /repo/scripts/apt/fake-github.py --branch "$1" --assets /work/rel --log /work/health.log --port 8081 &
    echo $! > /work/fg.pid
    for _ in $(seq 50); do curl -s -o /dev/null http://127.0.0.1:8081/ && break; sleep 0.2; done
  }
  health_run() { code=0; out="$(bash /repo/scripts/apt/check-apt-health.sh --base-url "$url" --require-sqv "$@" 2>&1)" || code=$?; }

  serve /work/branch
  health_run --keyring /work/key/archive.gpg
  echo "$out" | sed 's/^/      | /'
  check "health passes on the published state" test "$code" -eq 0

  mv /work/rel/v1.0.0-rc.4/orkeon_1.0.0-rc.4_amd64.deb /work/held.deb
  health_run --keyring /work/key/archive.gpg --no-download
  check "a stanza without its asset fails" bash -c '[ "$0" -ne 0 ] && grep -q "FAIL  rc: orkeon 1.0.0~rc.4 amd64 .*without its asset" <<<"$1"' "$code" "$out"
  mv /work/held.deb /work/rel/v1.0.0-rc.4/orkeon_1.0.0-rc.4_amd64.deb

  cp -a /work/branch /work/bad
  sed -i 's/^Label: Orkeon$/Label: Orkeon2/' /work/bad/rc/InRelease
  serve /work/bad
  health_run --keyring /work/key/archive.gpg --no-download
  check "an InRelease that does not verify fails" bash -c '[ "$0" -ne 0 ] && grep -q "FAIL  rc: InRelease does not verify with gpgv" <<<"$1"' "$code" "$out"

  make_key /work/key short 30d
  cp -a /work/branch /work/short
  for c in stable rc; do
    APT_SIGNING_KEY="$(cat /work/key/short-secret.asc)" APT_SIGNING_PASSPHRASE="$PASSPHRASE" \
      bash /repo/scripts/apt/build-apt-index.sh --channel "$c" --dir "/work/short/$c" --keyring /work/key/short.gpg 2>/dev/null
  done
  serve /work/short
  health_run --keyring /work/key/short.gpg --no-download
  check "a signing subkey expiring within 180 days fails" bash -c '[ "$0" -ne 0 ] && grep -q "FAIL  the signing subkey expires in 29 days" <<<"$1"' "$code" "$out"
  check "... while its signatures verify" bash -c 'grep -q "ok    rc: InRelease verifies with gpgv" <<<"$0" && grep -q "ok    rc: InRelease verifies with sqv" <<<"$0"' "$out"
  kill "$(cat /work/fg.pid)" 2>/dev/null || true

  if [ "$failed" -gt 0 ]; then echo "health: ${failed} check(s) failed"; exit 1; fi
  echo "health passed"
}

# --- On this machine -----------------------------------------------------------------------
dexec() { # <container> <command...>: run to completion, print the output, return the status
  local c="$1" tag="/tmp/dexec-$RANDOM$RANDOM" waited=0 st
  shift
  docker exec -d "$c" bash -c '"$@" > "$0.out" 2>&1; echo $? > "$0.rc"' "$tag" "$@"
  until st="$(docker cp "$c:$tag.rc" - 2>/dev/null | tar -xOf - 2>/dev/null)" && [ -n "$st" ]; do
    waited=$((waited + 1))
    [ "$waited" -lt 1800 ] || { echo "test-apt-publish: timed out in $c: $*" >&2; return 124; }
    sleep 1
  done
  docker cp "$c:$tag.out" - | tar -xOf -
  return "$st"
}

main() {
  command -v docker >/dev/null 2>&1 || { echo "test-apt-publish: needs docker" >&2; exit 1; }
  local b="apt-publish-builder-$$" out
  out="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "docker rm -f $b >/dev/null 2>&1 || true; rm -rf '$out'" EXIT
  docker run -d --name "$b" "$BUILDER_IMAGE" sleep infinity >/dev/null
  dexec "$b" bash -c 'apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends apt-utils gpg gpg-agent gpgv sqv git ca-certificates python3 curl >/dev/null' \
    || { echo "test-apt-publish: could not prepare $BUILDER_IMAGE" >&2; exit 1; }
  docker exec "$b" mkdir -p /repo/docs/fr
  tar -C "$REPO_ROOT" -cf - scripts installers docs/guides docs/fr/guides | docker exec -i "$b" tar -C /repo -xf -
  dexec "$b" bash /repo/scripts/apt/test-apt-publish.sh --builder || failed=$((failed + 1))

  docker cp "$b:/work/branch" "$out/branch"
  docker cp "$b:/work/rel" "$out/rel"
  for f in v1.0.0-rc.4 v1.0.0-rc.5; do
    docker cp "$b:/work/debs-$f.list" - | tar -xOf - | sed "s|^/work/|$out/|" > "$out/debs-$f.list"
  done
  cat "$out/debs-v1.0.0-rc.4.list" "$out/debs-v1.0.0-rc.5.list" > "$out/debs.list"

  # The page as it will read once the key exists: its SHA-256 is the throwaway key's.
  sha="$(sha256sum "$out/branch/orkeon-archive-keyring.gpg" | cut -d' ' -f1)"
  sed "s|<PENDING-KEY-CEREMONY>|$sha|" "$REPO_ROOT/docs/guides/install-with-apt.md" > "$out/page.md"

  echo "# the documented block (check-apt-branch.sh, verify-apt-repo.sh) on rc, client $CLIENT_IMAGE"
  if bash "$here/check-apt-branch.sh" --branch-dir "$out/branch" --debs "$out/debs.list" --previous auto \
      --page "$out/page.md" --channel rc --version 1.0.0-rc.5 --image "$CLIENT_IMAGE" --server-container "$b"; then
    echo "ok    check-apt-branch passes on the published rc"
  else
    echo "FAIL  check-apt-branch passes on the published rc"; failed=$((failed + 1))
  fi
  # A version the channel does not offer must fail.
  if bash "$here/check-apt-branch.sh" --branch-dir "$out/branch" --debs "$out/debs-v1.0.0-rc.5.list" \
      --page "$out/page.md" --channel rc --version 1.0.0-rc.6 --image "$CLIENT_IMAGE" --server-container "$b" >/dev/null 2>&1; then
    echo "FAIL  check-apt-branch fails on a version the channel lacks"; failed=$((failed + 1))
  else
    echo "ok    check-apt-branch fails on a version the channel lacks"
  fi
  # The page as committed (the key SHA-256 still pending) must fail: never in silence.
  if bash "$here/check-apt-branch.sh" --branch-dir "$out/branch" --debs "$out/debs-v1.0.0-rc.5.list" \
      --channel rc --version 1.0.0-rc.5 --image "$CLIENT_IMAGE" --server-container "$b" >/dev/null 2>&1; then
    echo "FAIL  the block refuses a key whose SHA-256 differs from the page's"; failed=$((failed + 1))
  else
    echo "ok    the block refuses a key whose SHA-256 differs from the page's"
  fi

  echo "# check-apt-health.sh against the fake GitHub"
  dexec "$b" bash /repo/scripts/apt/test-apt-publish.sh --health || failed=$((failed + 1))

  if [ "$failed" -gt 0 ]; then echo "test-apt-publish: ${failed} check(s) failed"; exit 1; fi
  echo "test-apt-publish passed"
}

case "${1:-}" in
  --builder) builder ;;
  --health) health ;;
  "") main ;;
  -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) echo "test-apt-publish: unknown argument: $1" >&2; exit 2 ;;
esac
