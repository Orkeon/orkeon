#!/usr/bin/env bash
# Publishes built channel directories to the `apt` branch: one commit, pushed without force.
# Every workflow that publishes the apt repository goes through here: release.yml (job
# apt-publish), apt-maintenance.yml and apt-dev.yml.
#
# Usage:
#   scripts/apt/publish-apt-channel.sh --channel <name> --dir <built dir>
#                                      [--channel <name> --dir <built dir>]...
#                                      --message <what> [--base <commit>]
#                                      [--root-keyring <binary .gpg>] [--repo-dir <checkout>]
#                                      [--branch apt] [--attempts N]
#   scripts/apt/publish-apt-channel.sh --read <dir> [--repo-dir <checkout>] [--branch apt]
#
#   --channel/--dir  each pair replaces exactly <channel>/ of the branch with the content of
#                    <dir>, deletions included. Nothing else of the branch is touched.
#   --message        what is published, e.g. the tag. The commit message is
#                    "apt: <changed channels, comma-separated> <what>".
#   --base           the apt commit the channel directories were read from (--read prints
#                    it). The publication is refused when one of the given channels changed
#                    on the remote since that commit: a signed index built from an older
#                    state would silently drop what was published meanwhile.
#   --root-keyring   replaces the root orkeon-archive-keyring.gpg (the file the installation
#                    block downloads). Without it the root file is left alone, except when the
#                    branch is created: then it is installers/apt/orkeon-archive-keyring.asc
#                    of the checkout, dearmored.
#   --repo-dir       the checkout whose `origin` is pushed to, with its credentials
#                    (default: the repository this script lives in).
#   --attempts       default 5.
#   --read <dir>     exports the current branch into <dir> (left empty when the branch does
#                    not exist yet) and prints its commit (nothing when it does not exist).
#
# Runs from a checkout that keeps its push credentials (actions/checkout does by default);
# the checkout's own branch, index and working tree are never modified (a private index file
# and a temporary work tree are used). First use: when the branch does not exist, it is
# created as an orphan holding README.md ("managed by CI"), .gitattributes ("* -text": no
# line-ending conversion may touch a signed file) and orkeon-archive-keyring.gpg.
#
# When the push is refused because the branch moved meanwhile (another channel published:
# non-fast-forward), the branch is fetched again, only the given channel directories are
# applied again on the new tip (still checked against --base), and the push is retried. Any
# other refusal stops at once. Exit 0 only once the push is done (or nothing changed).
# The commit author is github-actions[bot] unless GIT_AUTHOR_NAME / GIT_AUTHOR_EMAIL are set.
# Exit status: 0 published, 1 failure or refusal, 2 usage.
set -euo pipefail
export LC_ALL=C

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REPO_DIR="$REPO_ROOT"
CHANNELS=()
DIRS=()
MESSAGE=""
BASE=""
ROOT_KEYRING=""
BRANCH="apt"
ATTEMPTS=5
READ_DIR=""
BOT_NAME="github-actions[bot]"
BOT_EMAIL="41898282+github-actions[bot]@users.noreply.github.com"
TIP_REF="refs/orkeon-apt-publish/tip"

die()   { echo "publish-apt-channel: $*" >&2; exit 1; }
usage() { echo "publish-apt-channel: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --channel)      [[ $# -ge 2 ]] || usage "$1 needs a value"; CHANNELS+=("$2"); shift 2 ;;
    --dir)          [[ $# -ge 2 ]] || usage "$1 needs a value"; DIRS+=("$2"); shift 2 ;;
    --message)      [[ $# -ge 2 ]] || usage "$1 needs a value"; MESSAGE="$2"; shift 2 ;;
    --base)         [[ $# -ge 2 ]] || usage "$1 needs a value"; BASE="$2"; shift 2 ;;
    --root-keyring) [[ $# -ge 2 ]] || usage "$1 needs a value"; ROOT_KEYRING="$2"; shift 2 ;;
    --repo-dir)     [[ $# -ge 2 ]] || usage "$1 needs a value"; REPO_DIR="$2"; shift 2 ;;
    --branch)       [[ $# -ge 2 ]] || usage "$1 needs a value"; BRANCH="$2"; shift 2 ;;
    --attempts)     [[ $# -ge 2 ]] || usage "$1 needs a value"; ATTEMPTS="$2"; shift 2 ;;
    --read)         [[ $# -ge 2 ]] || usage "$1 needs a value"; READ_DIR="$2"; shift 2 ;;
    -h|--help) sed -n '2,45p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done
[[ "$BRANCH" =~ ^[A-Za-z0-9][A-Za-z0-9._/-]*$ ]] || usage "invalid branch name '$BRANCH'"
git -C "$REPO_DIR" rev-parse --git-dir >/dev/null 2>&1 || usage "--repo-dir is not a git checkout: $REPO_DIR"
REPO_DIR="$(cd "$REPO_DIR" && pwd)"
GIT_DIR_ABS="$(cd "$REPO_DIR" && cd "$(git rev-parse --git-dir)" && pwd)"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
export GIT_INDEX_FILE="$WORK/index"
g() { git --git-dir="$GIT_DIR_ABS" -c core.autocrlf=false -c core.quotepath=off "$@"; }

# fetch_tip: the current remote tip into $TIP_REF; prints it (nothing when the branch is absent)
fetch_tip() {
  local tip
  tip="$(cd "$REPO_DIR" && g ls-remote --heads origin "refs/heads/$BRANCH")" || die "cannot read the origin of $REPO_DIR"
  if [[ -z "$tip" ]]; then
    g update-ref -d "$TIP_REF" 2>/dev/null || true
    return 0
  fi
  ( cd "$REPO_DIR" && g fetch -q --no-tags --depth 1 origin "+refs/heads/$BRANCH:$TIP_REF" ) || die "cannot fetch $BRANCH"
  g rev-parse "$TIP_REF"
}

# --- --read -------------------------------------------------------------------------------
if [[ -n "$READ_DIR" ]]; then
  [[ ${#CHANNELS[@]} -eq 0 ]] || usage "--read takes no --channel"
  mkdir -p "$READ_DIR"
  tip="$(fetch_tip)"
  if [[ -n "$tip" ]]; then
    g archive "$tip" | tar -x -C "$READ_DIR"
    echo "$tip"
  fi
  exit 0
fi

# --- publication: arguments ------------------------------------------------------------------
[[ ${#CHANNELS[@]} -ge 1 ]] || usage "at least one --channel is required"
[[ ${#CHANNELS[@]} -eq ${#DIRS[@]} ]] || usage "give one --dir per --channel, in the same order"
[[ -n "$MESSAGE" ]] || usage "--message is required"
[[ "$ATTEMPTS" =~ ^[1-9][0-9]*$ ]] || usage "--attempts takes a positive number"
for i in "${!CHANNELS[@]}"; do
  [[ "${CHANNELS[$i]}" =~ ^[a-z][a-z0-9-]*$ ]] || usage "invalid channel name '${CHANNELS[$i]}'"
  [[ -d "${DIRS[$i]}" ]] || die "not a directory: ${DIRS[$i]}"
  DIRS[i]="$(cd "${DIRS[$i]}" && pwd)"
done
if [[ -n "$BASE" ]]; then
  [[ "$BASE" =~ ^[0-9a-f]{40}$ ]] || usage "--base takes a full commit id"
fi
if [[ -n "$ROOT_KEYRING" ]]; then
  [[ -f "$ROOT_KEYRING" ]] || die "root keyring not found: $ROOT_KEYRING"
  if head -c 64 "$ROOT_KEYRING" | grep -q -- '-----BEGIN PGP'; then
    die "$ROOT_KEYRING is armored: the installation block installs it as a .gpg, which apt 2.4-2.8 must read as binary"
  fi
fi

# subtree <commit-or-tree> <channel>: the tree id of <channel>/, empty when absent
subtree() { [[ -n "$1" ]] && g rev-parse -q --verify "$1:$2" 2>/dev/null || true; }

# check_base <tip>: refuse when a given channel changed between --base and <tip>
check_base() {
  local tip="$1" c
  [[ -n "$BASE" ]] || return 0
  [[ -n "$tip" ]] || die "refused: --base $BASE given, but the $BRANCH branch does not exist on the remote"
  if ! g cat-file -e "$BASE^{commit}" 2>/dev/null; then
    ( cd "$REPO_DIR" && g fetch -q --no-tags --depth 1 origin "$BASE" ) 2>/dev/null \
      || die "refused: --base $BASE cannot be fetched from the remote"
  fi
  for c in "${CHANNELS[@]}"; do
    if [[ "$(subtree "$BASE" "$c")" != "$(subtree "$tip" "$c")" ]]; then
      die "refused: $c/ changed on the remote since $BASE (now $tip); build it again from the current branch. Nothing pushed."
    fi
  done
}

write_bootstrap_files() { # <work tree>
  cat > "$1/README.md" <<'EOF'
# The Orkeon apt repository

This branch is managed by CI — do not modify it by hand, and never force-push it: its
history is the log of every publication.

Each directory is a channel (`stable`, `rc`, `dev`) holding a signed flat apt index
(`InRelease`, `Release`, `Release.gpg`, `Packages`, `Packages.gz`, `by-hash/`). The packages
themselves are the assets of the GitHub Releases; `orkeon-archive-keyring.gpg` is the
repository's public key.

To install Orkeon with apt, follow
[Install with apt](https://github.com/Orkeon/orkeon/blob/main/docs/guides/install-with-apt.md).
EOF
  printf '* -text\n' > "$1/.gitattributes"
  if [[ -z "$ROOT_KEYRING" ]]; then
    local asc="$REPO_DIR/installers/apt/orkeon-archive-keyring.asc" home
    [[ -f "$asc" ]] || die "the $BRANCH branch does not exist yet and $asc is missing: give --root-keyring"
    home="$(mktemp -d)"
    GNUPGHOME="$home" gpg --batch --dearmor < "$asc" > "$1/orkeon-archive-keyring.gpg"
    rm -rf "$home"
  fi
}

attempt=1
while :; do
  tip="$(fetch_tip)"
  check_base "$tip"
  wt="$WORK/tree"
  rm -rf "$wt" "$GIT_INDEX_FILE"
  mkdir -p "$wt"
  if [[ -n "$tip" ]]; then
    g read-tree "$tip"
    state="tip ${tip:0:7}"
  else
    g read-tree --empty
    write_bootstrap_files "$wt"
    state="new orphan branch"
  fi
  [[ -z "$ROOT_KEYRING" ]] || cp "$ROOT_KEYRING" "$wt/orkeon-archive-keyring.gpg"
  for i in "${!CHANNELS[@]}"; do
    c="${CHANNELS[$i]}"
    g rm -r -q --cached --ignore-unmatch -- "$c" >/dev/null
    mkdir -p "$wt/$c"
    cp -a "${DIRS[$i]}/." "$wt/$c/"
  done
  # Only what this run wrote is staged: the given channels and the root files it wrote.
  # Everything else stays as the index read it from the tip.
  paths=("${CHANNELS[@]}")
  for f in README.md .gitattributes orkeon-archive-keyring.gpg; do
    [[ ! -e "$wt/$f" ]] || paths+=("$f")
  done
  ( cd "$wt" && GIT_WORK_TREE="$wt" g add -A -f -- "${paths[@]}" )
  echo "publish-apt-channel: attempt $attempt/$ATTEMPTS on $BRANCH ($state)" >&2

  tree="$(g write-tree)"
  if [[ -n "$tip" && "$tree" == "$(g rev-parse "$tip^{tree}")" ]]; then
    echo "publish-apt-channel: nothing changed, nothing to publish" >&2
    exit 0
  fi
  changed=()
  for c in "${CHANNELS[@]}"; do
    [[ "$(subtree "$tree" "$c")" == "$(subtree "$tip" "$c")" ]] || changed+=("$c")
  done
  subject="apt: $(IFS=,; echo "${changed[*]:-root}") $MESSAGE"
  author_name="${GIT_AUTHOR_NAME:-$BOT_NAME}"
  author_email="${GIT_AUTHOR_EMAIL:-$BOT_EMAIL}"
  parent=()
  [[ -z "$tip" ]] || parent=(-p "$tip")
  commit="$(GIT_AUTHOR_NAME="$author_name" GIT_AUTHOR_EMAIL="$author_email" \
            GIT_COMMITTER_NAME="${GIT_COMMITTER_NAME:-$author_name}" \
            GIT_COMMITTER_EMAIL="${GIT_COMMITTER_EMAIL:-$author_email}" \
            g commit-tree "$tree" ${parent[@]+"${parent[@]}"} -m "$subject")"
  echo "publish-apt-channel: commit \"$subject\" (${commit:0:7})" >&2

  push_log="$WORK/push.log"
  if ( cd "$REPO_DIR" && g push origin "$commit:refs/heads/$BRANCH" ) >"$push_log" 2>&1; then
    cat "$push_log" >&2
    echo "publish-apt-channel: pushed $BRANCH ($commit)" >&2
    exit 0
  fi
  cat "$push_log" >&2
  if ! grep -qiE 'non-fast-forward|fetch first|stale info|cannot lock ref' "$push_log"; then
    die "push refused for another reason than a moved branch. Nothing more is tried."
  fi
  if (( attempt >= ATTEMPTS )); then
    die "the $BRANCH branch kept moving: gave up after $ATTEMPTS attempts. Nothing pushed."
  fi
  wait_s=$(( 2 ** attempt + RANDOM % 5 ))
  echo "publish-apt-channel: $BRANCH moved meanwhile; applying ${CHANNELS[*]} again on the new tip in ${wait_s}s" >&2
  sleep "$wait_s"
  attempt=$((attempt + 1))
done
