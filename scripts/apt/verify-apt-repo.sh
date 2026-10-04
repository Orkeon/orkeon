#!/usr/bin/env bash
# Replays the installation block of docs/guides/install-with-apt.md, word for word, in a
# fresh container, then checks what a user gets: the version, the packages' integrity, an
# upgrade from the previous version, and a removal in the documented order.
#
# Usage:
#   scripts/apt/verify-apt-repo.sh <channel> <expected version> [options]
#
#   <channel>           stable, rc or dev: the one line of the block rewritten is
#                       "Suites: raw/apt/<channel>/". Everything else runs as written,
#                       under bash -euo pipefail, as a non-root sudoer.
#   <expected version>  as tagged (1.0.0-rc.5); the package version is 1.0.0~rc.5.
#   --image IMAGE       the container (default debian:13); pin it by digest in CI.
#   --previous auto|none
#                       auto (default): the highest earlier version the channel offers
#                       for this architecture is installed first, then `apt-get upgrade`
#                       must bring the expected one back. SKIP, with its reason, when there
#                       is none or it is not installable on this distribution.
#   --attempts N --wait S
#                       the block (key download and apt-get update included) and the
#                       wait for the expected version are tried N times, S seconds apart
#                       (default 6 x 30 s): raw.githubusercontent.com may serve the
#                       previous index for minutes after a push, and answer 429.
#   --page FILE         the page to take the block from (tests).
#   --github-url URL    tests only: replaces https://github.com/ in the block (the fake
#                       GitHub of fake-github.py).
#   --docker-arg ARG    passed to `docker create` (repeatable; tests: --add-host).
#   --docker-network N  a docker network the container joins too (tests).
#
# Every apt-get update must print no W: or E: line ("Conflicting distribution", a key
# warning, a failed fetch): the last attempt fails on one. The report is PASS / SKIP / FAIL
# per step (scripts/smoke-onboarding/lib/smoke-common.sh); exit 0 only without FAIL.
# Needs docker. The container runs the image's architecture on this machine.
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$here/../.." && pwd)"

# ============================================================================================
# Inside the container (root): bash /v/verify-apt-repo.sh --inside
# ============================================================================================
inside() {
  # shellcheck source=../smoke-onboarding/lib/smoke-common.sh
  source /v/smoke-common.sh
  smoke_init_bookkeeping
  export DEBIAN_FRONTEND=noninteractive
  LOG_DIR=/v/logs
  RUN_DIR=/tmp/run
  mkdir -p "$LOG_DIR" "$RUN_DIR"
  local banner="APT VERIFY $CHANNEL $EXPECTED ($IMAGE_NAME)"
  local deb_version="${EXPECTED//-/\~}" i out code ok

  finish() { smoke_summary "$banner"; exit $?; }
  clean() { ! grep -qE '^(W|E|Err):' "$1"; }
  apt_update() { # <log>: apt-get update, clean or not
    apt-get update >"$1" 2>&1 && clean "$1"
  }

  smoke_log "Container: sudo and an unprivileged user"
  ok=false
  for i in 1 2 3; do
    if apt-get update -qq >"$LOG_DIR/prepare.log" 2>&1 \
       && apt-get install -y -qq --no-install-recommends sudo >>"$LOG_DIR/prepare.log" 2>&1; then
      ok=true; break
    fi
    sleep 10
  done
  if ! $ok; then
    tail -n 20 "$LOG_DIR/prepare.log" | sed 's/^/    | /'
    smoke_fail "container" "sudo could not be installed from the distribution's mirrors"
    finish
  fi
  useradd -m -s /bin/bash tester
  echo 'tester ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/tester
  chmod 0440 /etc/sudoers.d/tester
  smoke_pass "container" "$(. /etc/os-release && echo "$PRETTY_NAME"), $(dpkg --print-architecture), user tester (sudo)"

  smoke_log "The documented block, as tester"
  sed 's/^/    | /' /v/block.sh
  ok=false
  for ((i = 1; i <= ATTEMPTS; i++)); do
    code=0
    sudo -u tester -H bash -euo pipefail /v/block.sh >"$LOG_DIR/block.log" 2>&1 || code=$?
    if [[ "$code" -eq 0 ]] && clean "$LOG_DIR/block.log"; then ok=true; break; fi
    smoke_info "attempt $i/$ATTEMPTS: exit $code$(clean "$LOG_DIR/block.log" || echo ', W:/E: lines')"
    grep -E '^(W|E|Err):' "$LOG_DIR/block.log" | head -n 5 | sed 's/^/    | /' || true
    (( i < ATTEMPTS )) && sleep "$WAIT"
  done
  if ! $ok; then
    tail -n 30 "$LOG_DIR/block.log" | sed 's/^/    | /'
    smoke_fail "block" "the installation block failed, or printed a W:/E: line, $ATTEMPTS times"
    finish
  fi
  smoke_pass "block" "ran as written (Suites: raw/apt/$CHANNEL/), no W: or E: line"

  smoke_log "The expected version"
  ok=false
  for ((i = 1; i <= ATTEMPTS; i++)); do
    if apt-cache madison orkeon | awk -F'|' '{gsub(/ /, "", $2); print $2}' | grep -qxF "$deb_version"; then ok=true; break; fi
    smoke_info "attempt $i/$ATTEMPTS: $deb_version not visible yet (raw may still serve the previous index)"
    (( i < ATTEMPTS )) || break
    sleep "$WAIT"
    apt_update "$LOG_DIR/update-visible.log" || grep -E '^(W|E|Err):' "$LOG_DIR/update-visible.log" | sed 's/^/    | /' || true
  done
  if ! $ok; then
    smoke_fail "visible" "the channel does not offer orkeon $deb_version"
    finish
  fi
  candidate="$(apt-cache policy orkeon | awk '/Candidate:/ { print $2 }')"
  if [[ "$candidate" == "$deb_version" ]]; then
    smoke_pass "visible" "orkeon $deb_version is the candidate"
  else
    smoke_fail "visible" "orkeon $deb_version is offered, but the candidate is $candidate"
  fi
  if [[ "$(dpkg-query -W -f '${Version}' orkeon 2>/dev/null)" != "$deb_version" ]]; then
    apt-get install -y orkeon >"$LOG_DIR/install-again.log" 2>&1 || true
  fi

  installed="$(dpkg-query -W -f '${Version}' orkeon 2>/dev/null || true)"
  line="$(orkeon --version 2>&1 </dev/null | head -n1 || true)"
  if [[ "$installed" == "$deb_version" && "$line" == "orkeon $EXPECTED" ]]; then
    smoke_pass "version" "$line (package $installed)"
  else
    smoke_fail "version" "installed '$installed', orkeon --version '$line'; expected $deb_version and 'orkeon $EXPECTED'"
  fi
  smoke_step_studio_present /usr/bin

  smoke_log "Integrity"
  if [[ ! -f /var/lib/dpkg/info/orkeon.md5sums ]]; then
    smoke_fail "dpkg-verify" "the package ships no md5sums: dpkg -V orkeon checks nothing"
  elif out="$(dpkg -V orkeon 2>&1)" && [[ -z "$out" ]]; then
    smoke_pass "dpkg-verify" "dpkg -V orkeon is silent"
  else
    printf '%s\n' "$out" | sed 's/^/    | /'
    smoke_fail "dpkg-verify" "dpkg -V orkeon reported differences"
  fi
  if [[ "$(dpkg-query -W -f '${Status}' orkeon-archive-keyring 2>/dev/null)" == "install ok installed" ]]; then
    smoke_pass "keyring" "orkeon-archive-keyring $(dpkg-query -W -f '${Version}' orkeon-archive-keyring) installed"
  else
    smoke_fail "keyring" "orkeon-archive-keyring is not installed"
  fi
  apt-cache policy orkeon > "$LOG_DIR/policy.log"
  if grep -qF "$GITHUB_BASE/Orkeon/orkeon raw/apt/$CHANNEL/" "$LOG_DIR/policy.log"; then
    smoke_pass "policy" "apt-cache policy orkeon shows $GITHUB_BASE/Orkeon/orkeon raw/apt/$CHANNEL/"
  else
    sed 's/^/    | /' "$LOG_DIR/policy.log"
    smoke_fail "policy" "apt-cache policy orkeon does not show the GitHub source"
  fi

  smoke_log "Upgrade from the previous version"
  previous=""
  if [[ "$PREVIOUS" == none ]]; then
    smoke_skip "upgrade" "--previous none"
  else
    while IFS= read -r v; do
      if dpkg --compare-versions "$v" lt "$deb_version" && { [[ -z "$previous" ]] || dpkg --compare-versions "$v" gt "$previous"; }; then
        previous="$v"
      fi
    done < <(apt-cache madison orkeon | awk -F'|' '{gsub(/ /, "", $2); print $2}' | sort -u)
    if [[ -z "$previous" ]]; then
      smoke_skip "upgrade" "the channel offers no earlier version for $(dpkg --print-architecture)"
    elif ! apt-get install -s --allow-downgrades "orkeon=$previous" >"$LOG_DIR/previous-sim.log" 2>&1; then
      smoke_skip "upgrade" "orkeon $previous is not installable on this distribution ($(grep -m1 -E 'Depends|E:' "$LOG_DIR/previous-sim.log" | sed 's/^ *//'))"
    elif ! apt-get install -y --allow-downgrades "orkeon=$previous" >"$LOG_DIR/previous.log" 2>&1; then
      tail -n 15 "$LOG_DIR/previous.log" | sed 's/^/    | /'
      smoke_fail "upgrade" "installing orkeon=$previous failed"
    elif ! apt_update "$LOG_DIR/update-upgrade.log"; then
      grep -E '^(W|E|Err):' "$LOG_DIR/update-upgrade.log" | sed 's/^/    | /'
      smoke_fail "upgrade" "apt-get update before the upgrade printed a W:/E: line"
    else
      apt-get upgrade -y >"$LOG_DIR/upgrade.log" 2>&1 || true
      now="$(dpkg-query -W -f '${Version}' orkeon 2>/dev/null || true)"
      if [[ "$now" == "$deb_version" ]]; then
        smoke_pass "upgrade" "$previous -> $now by apt-get upgrade"
      else
        tail -n 15 "$LOG_DIR/upgrade.log" | sed 's/^/    | /'
        smoke_fail "upgrade" "after apt-get upgrade from $previous, orkeon is $now (expected $deb_version)"
      fi
    fi
  fi

  smoke_log "Removal, in the documented order"
  rm -f /etc/apt/sources.list.d/orkeon.sources
  if apt-get purge -y orkeon orkeon-archive-keyring >"$LOG_DIR/purge.log" 2>&1; then
    left="$(compgen -G '/usr/lib/orkeon*' || true) $(compgen -G '/usr/bin/orkeon*' || true)"
    [[ -e /usr/share/keyrings/orkeon-archive-keyring.gpg ]] && left="$left /usr/share/keyrings/orkeon-archive-keyring.gpg"
    if [[ -z "${left// /}" ]]; then
      smoke_pass "remove" "purged; nothing left under /usr/lib/orkeon*, /usr/bin/orkeon*, the keyring"
    else
      smoke_fail "remove" "left behind:$left"
    fi
  else
    tail -n 15 "$LOG_DIR/purge.log" | sed 's/^/    | /'
    smoke_fail "remove" "apt-get purge failed"
  fi
  if apt_update "$LOG_DIR/update-after.log"; then
    smoke_pass "update-after" "apt-get update is clean once the source and the key are gone"
  else
    grep -E '^(W|E|Err):' "$LOG_DIR/update-after.log" | sed 's/^/    | /'
    smoke_fail "update-after" "apt-get update printed a W:/E: line after the removal"
  fi
  finish
}

if [[ "${1:-}" == --inside ]]; then
  inside
fi

# ============================================================================================
# On this machine
# ============================================================================================
die()   { echo "verify-apt-repo: $*" >&2; exit 1; }
usage() { echo "verify-apt-repo: $*" >&2; exit 2; }

[[ $# -ge 2 ]] || usage "usage: verify-apt-repo.sh <channel> <expected version> [options]"
CHANNEL="$1"; EXPECTED="$2"; shift 2
IMAGE="debian:13"
PREVIOUS="auto"
ATTEMPTS=6
WAIT=30
PAGE="$REPO_ROOT/docs/guides/install-with-apt.md"
GITHUB_URL=""
DOCKER_ARGS=()
NETWORKS=()
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage "$1 needs a value"
  case "$1" in
    --image)          IMAGE="$2" ;;
    --previous)       PREVIOUS="$2" ;;
    --attempts)       ATTEMPTS="$2" ;;
    --wait)           WAIT="$2" ;;
    --page)           PAGE="$2" ;;
    --github-url)     GITHUB_URL="$2" ;;
    --docker-arg)     DOCKER_ARGS+=("$2") ;;
    --docker-network) NETWORKS+=("$2") ;;
    *) usage "unknown argument: $1" ;;
  esac
  shift 2
done
[[ "$CHANNEL" =~ ^[a-z][a-z0-9-]*$ ]] || usage "invalid channel '$CHANNEL'"
[[ "$EXPECTED" =~ ^[0-9][A-Za-z0-9.+-]*$ ]] || usage "the expected version is the tag without v (1.0.0-rc.5)"
[[ "$PREVIOUS" == auto || "$PREVIOUS" == none ]] || usage "--previous takes auto or none"
[[ "$ATTEMPTS" =~ ^[1-9][0-9]*$ && "$WAIT" =~ ^[0-9]+$ ]] || usage "--attempts and --wait take numbers"
[[ -f "$PAGE" ]] || die "no such page: $PAGE"
command -v docker >/dev/null 2>&1 || die "docker not found"

WORK="$(mktemp -d)"
name="apt-verify-$$-$RANDOM"
trap 'docker rm -f "$name" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT
mkdir -p "$WORK/v"

# The block between the markers, without its code fence.
awk '/<!-- apt-setup:begin -->/ { on = 1; next } /<!-- apt-setup:end -->/ { on = 0 } on && !/^```/' "$PAGE" > "$WORK/block.orig"
[[ -s "$WORK/block.orig" ]] || die "$PAGE has no block between <!-- apt-setup:begin --> and <!-- apt-setup:end -->"
[[ "$(grep -c '^Suites: ' "$WORK/block.orig")" -eq 1 ]] || die "the block must hold exactly one 'Suites:' line"
sed "s|^Suites: .*|Suites: raw/apt/$CHANNEL/|" "$WORK/block.orig" > "$WORK/v/block.sh"
base="https://github.com"
if [[ -n "$GITHUB_URL" ]]; then
  sed -i "s|https://github.com/|${GITHUB_URL%/}/|g" "$WORK/v/block.sh"
  base="${GITHUB_URL%/}"
  echo "verify-apt-repo: TEST MODE, https://github.com/ is replaced by $GITHUB_URL" >&2
fi
cp "$0" "$WORK/v/verify-apt-repo.sh"
cp "$REPO_ROOT/scripts/smoke-onboarding/lib/smoke-common.sh" "$WORK/v/smoke-common.sh"

docker create --name "$name" \
  -e "CHANNEL=$CHANNEL" -e "EXPECTED=$EXPECTED" -e "PREVIOUS=$PREVIOUS" -e "ATTEMPTS=$ATTEMPTS" \
  -e "WAIT=$WAIT" -e "GITHUB_BASE=$base" -e "IMAGE_NAME=${IMAGE%%@*}" \
  ${DOCKER_ARGS[@]+"${DOCKER_ARGS[@]}"} "$IMAGE" bash /v/verify-apt-repo.sh --inside >/dev/null
for n in ${NETWORKS[@]+"${NETWORKS[@]}"}; do docker network connect "$n" "$name"; done
docker cp "$WORK/v" "$name:/v"
echo "verify-apt-repo: $CHANNEL $EXPECTED in $IMAGE" >&2
docker start "$name" >/dev/null
status="$(docker wait "$name")"
docker logs "$name" 2>&1
exit "$status"
