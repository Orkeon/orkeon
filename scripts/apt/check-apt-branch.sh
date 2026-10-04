#!/usr/bin/env bash
# Proves a channel of a checked-out `apt` branch with a real apt client BEFORE it is pushed:
# the branch and the packages are served by the fake GitHub (fake-github.py, same URLs and
# cross-host redirects as github.com), and a fresh container uses the documented source.
#
# Usage:
#   scripts/apt/check-apt-branch.sh --branch-dir <dir> --debs <debs.list> --channel <name>
#                                   --version <upstream version> [--image <image>]
#                                   [--port <n>]
#
#   --debs     "<file.deb>=<tag>/<asset>" lines (fetch-release-debs.sh): the packages that
#              may be downloaded. Each stanza of the channel whose SHA-256 matches one of
#              them is served at its Filename; the others answer 404 (nothing fetches them).
#   --version  the version the channel must offer, as tagged (1.0.0-rc.5: the package
#              version is 1.0.0~rc.5).
#   --image    the client (default ubuntu:24.04); it runs the runner's architecture.
#   --port     the fake GitHub's port (default 8080).
#   --server-container <name>
#              run the fake GitHub inside that running container (it needs python3) on a
#              private docker network, rather than on this machine: for a docker daemon
#              that cannot reach this machine's ports (Docker Desktop under WSL).
#
# The client trusts the branch's root orkeon-archive-keyring.gpg, declares
# installers/apt/orkeon.sources with the channel's Suites: line and the URI pointed at the
# fake GitHub, and only that source. It must then: update with no W:, E: or Err: line;
# offer the version; download orkeon=<version> for its architecture and, when the channel
# lists it, orkeon-archive-keyring - through the redirects, the bytes checked by apt against
# the signed index. Needs docker and python3.
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$here/../.." && pwd)"
BRANCH_DIR=""
DEBS_FILE=""
CHANNEL=""
VERSION=""
IMAGE="ubuntu:24.04"
PORT=8080
SERVER_CONTAINER=""

die()   { echo "check-apt-branch: $*" >&2; exit 1; }
usage() { echo "check-apt-branch: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --branch-dir) [[ $# -ge 2 ]] || usage "$1 needs a value"; BRANCH_DIR="$2"; shift 2 ;;
    --debs)       [[ $# -ge 2 ]] || usage "$1 needs a value"; DEBS_FILE="$2"; shift 2 ;;
    --channel)    [[ $# -ge 2 ]] || usage "$1 needs a value"; CHANNEL="$2"; shift 2 ;;
    --version)    [[ $# -ge 2 ]] || usage "$1 needs a value"; VERSION="$2"; shift 2 ;;
    --image)      [[ $# -ge 2 ]] || usage "$1 needs a value"; IMAGE="$2"; shift 2 ;;
    --port)       [[ $# -ge 2 ]] || usage "$1 needs a value"; PORT="$2"; shift 2 ;;
    --server-container) [[ $# -ge 2 ]] || usage "$1 needs a value"; SERVER_CONTAINER="$2"; shift 2 ;;
    -h|--help) sed -n '2,30p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done
[[ -d "$BRANCH_DIR/$CHANNEL" && -f "$BRANCH_DIR/$CHANNEL/Packages" ]] || usage "--branch-dir/--channel must name a built channel"
[[ -f "$DEBS_FILE" ]] || usage "--debs must name a debs.list"
[[ "$VERSION" =~ ^[0-9][A-Za-z0-9.+-]*$ ]] || usage "--version takes the upstream version (1.0.0-rc.5)"
[[ -f "$BRANCH_DIR/orkeon-archive-keyring.gpg" ]] || die "the branch has no root orkeon-archive-keyring.gpg"
command -v docker >/dev/null 2>&1 || die "docker not found"
command -v python3 >/dev/null 2>&1 || die "python3 not found"
BRANCH_DIR="$(cd "$BRANCH_DIR" && pwd)"
DEB_VERSION="${VERSION//-/\~}"

WORK="$(mktemp -d)"
name="apt-check-$$"
server_pid=""
net=""
cleanup() {
  docker rm -f "$name" >/dev/null 2>&1 || true
  [[ -z "$server_pid" ]] || kill "$server_pid" 2>/dev/null || true
  if [[ -n "$SERVER_CONTAINER" ]]; then
    docker exec "$SERVER_CONTAINER" bash -c 'kill "$(cat /fg/pid)"; rm -rf /fg' >/dev/null 2>&1 || true
    [[ -z "$net" ]] || { docker network disconnect "$net" "$SERVER_CONTAINER" >/dev/null 2>&1; docker network rm "$net" >/dev/null 2>&1; } || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# --- 1. The assets the channel's stanzas name, when we hold their bytes ------------------
declare -A by_sha=()
while IFS= read -r spec; do
  [[ -n "$spec" ]] || continue
  file="${spec%=*}"
  by_sha["$(sha256sum "$file" | cut -d' ' -f1)"]="$file"
done < "$DEBS_FILE"
served=0
while IFS=' ' read -r filename sha; do
  [[ "$filename" == releases/download/*/* ]] || die "unexpected Filename: $filename"
  rel="${filename#releases/download/}"
  if [[ -n "${by_sha[$sha]:-}" ]]; then
    mkdir -p "$WORK/assets/${rel%%/*}"
    cp "${by_sha[$sha]}" "$WORK/assets/$rel"
    served=$((served + 1))
  fi
done < <(awk '/^Filename: / { f = $2 } /^SHA256: / { s = $2 } /^$/ { if (f) print f, s; f = s = "" } END { if (f) print f, s }' "$BRANCH_DIR/$CHANNEL/Packages")
[[ "$served" -ge 1 ]] || die "no stanza of $CHANNEL/Packages matches the given packages"
echo "check-apt-branch: serving $CHANNEL with $served package(s) on port $PORT" >&2

# --- 2. The fake GitHub -----------------------------------------------------------------
hosts=(--add-host github.test:host-gateway --add-host raw.test:host-gateway --add-host objects.test:host-gateway)
netargs=()
if [[ -z "$SERVER_CONTAINER" ]]; then
  python3 "$here/fake-github.py" --branch "$BRANCH_DIR" --assets "$WORK/assets" \
    --log "$WORK/requests.log" --port "$PORT" &
  server_pid=$!
  for _ in $(seq 50); do
    curl -s -o /dev/null "http://127.0.0.1:$PORT/" && break
    sleep 0.2
  done
else
  docker exec "$SERVER_CONTAINER" rm -rf /fg
  docker exec "$SERVER_CONTAINER" mkdir -p /fg/assets
  docker cp "$here/fake-github.py" "$SERVER_CONTAINER:/fg/fake-github.py"
  docker cp "$BRANCH_DIR/." "$SERVER_CONTAINER:/fg/branch"
  docker cp "$WORK/assets/." "$SERVER_CONTAINER:/fg/assets"
  docker exec -d "$SERVER_CONTAINER" bash -c 'echo $$ > /fg/pid; exec python3 /fg/fake-github.py --branch /fg/branch \
    --assets /fg/assets --log /fg/requests.log --port "$0"' "$PORT"
  net="apt-check-net-$$"
  docker network create --internal "$net" >/dev/null
  docker network connect "$net" "$SERVER_CONTAINER"
  ip="$(docker inspect -f "{{(index .NetworkSettings.Networks \"$net\").IPAddress}}" "$SERVER_CONTAINER")"
  hosts=(--add-host "github.test:$ip" --add-host "raw.test:$ip" --add-host "objects.test:$ip")
  netargs=(--network "$net")
  sleep 2
fi

# --- 3. The client -----------------------------------------------------------------------
sed -e "s|https://github.com/|http://github.test:$PORT/|" -e "s|^Suites: .*|Suites: raw/apt/$CHANNEL/|" \
  "$REPO_ROOT/installers/apt/orkeon.sources" > "$WORK/orkeon.sources"
grep -qx "Suites: raw/apt/$CHANNEL/" "$WORK/orkeon.sources" || die "installers/apt/orkeon.sources has no Suites: line"
cp "$BRANCH_DIR/orkeon-archive-keyring.gpg" "$WORK/keyring.gpg"
cat > "$WORK/client.sh" <<'EOF'
set -uo pipefail
export DEBIAN_FRONTEND=noninteractive
fail=0
ok()  { echo "ok    $*"; }
bad() { echo "FAIL  $*"; fail=1; }
rm -f /etc/apt/sources.list /etc/apt/sources.list.d/*
echo 'Acquire::Retries "0";' > /etc/apt/apt.conf.d/99check
install -m 0644 /in/keyring.gpg /usr/share/keyrings/orkeon-archive-keyring.gpg
install -m 0644 /in/orkeon.sources /etc/apt/sources.list.d/orkeon.sources
out="$(apt-get update 2>&1)"; code=$?
echo "$out" | sed 's/^/      | /'
[ "$code" -eq 0 ] && ok "apt-get update exits 0" || bad "apt-get update exits $code"
if grep -qE '^(W|E|Err):' <<<"$out"; then bad "apt-get update printed a W:, E: or Err: line"; else ok "apt-get update printed no W:, E: or Err: line"; fi
arch="$(dpkg --print-architecture)"
if apt-cache madison orkeon | grep -q " $DEB_VERSION "; then ok "the channel offers orkeon $DEB_VERSION"; else bad "the channel does not offer orkeon $DEB_VERSION ($arch)"; fi
cd /tmp
if apt-get download "orkeon=$DEB_VERSION" >/tmp/dl.log 2>&1 && [ "$(dpkg-deb -f orkeon_*_"$arch".deb Version)" = "$DEB_VERSION" ]; then
  ok "orkeon $DEB_VERSION ($arch) downloads, hash checked against the signed index"
else
  sed 's/^/      | /' /tmp/dl.log; bad "orkeon $DEB_VERSION ($arch) does not download"
fi
if apt-cache show orkeon-archive-keyring >/dev/null 2>&1; then
  if apt-get download orkeon-archive-keyring >/tmp/dl.log 2>&1; then ok "orkeon-archive-keyring downloads"; else sed 's/^/      | /' /tmp/dl.log; bad "orkeon-archive-keyring does not download"; fi
fi
exit "$fail"
EOF

docker create --name "$name" -e "DEB_VERSION=$DEB_VERSION" "${hosts[@]}" ${netargs[@]+"${netargs[@]}"} \
  "$IMAGE" bash /in/client.sh >/dev/null
mkdir -p "$WORK/in"
cp "$WORK/client.sh" "$WORK/orkeon.sources" "$WORK/keyring.gpg" "$WORK/in/"
docker cp "$WORK/in" "$name:/in"
docker start "$name" >/dev/null
status="$(docker wait "$name")"
docker logs "$name" 2>&1
if [[ -n "$SERVER_CONTAINER" ]]; then
  docker cp "$SERVER_CONTAINER:/fg/requests.log" - 2>/dev/null | tar -xOf - > "$WORK/requests.log" || true
fi
echo "check-apt-branch: what the fake GitHub served" >&2
sed 's/^/      | /' "$WORK/requests.log" >&2
if grep -qE '^404 ' "$WORK/requests.log"; then
  echo "FAIL  a request answered 404"; status=1
fi
if grep -qi '%7e' "$WORK/requests.log"; then
  echo "FAIL  apt requested a %7e"; status=1
fi
[[ "$status" -eq 0 ]] || die "$CHANNEL failed the pre-push check against $IMAGE. Nothing may be pushed."
echo "check-apt-branch: $CHANNEL passed against $IMAGE" >&2
