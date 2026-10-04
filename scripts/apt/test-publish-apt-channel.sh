#!/usr/bin/env bash
# Bench of scripts/apt/publish-apt-channel.sh against a local bare repository: git and gpg
# only, no network, no key, no apt tool (the channel directories here hold plain files).
#
# Usage: scripts/apt/test-publish-apt-channel.sh
#
# Proves: --read on a missing and on an existing branch; the orphan bootstrap (README.md,
# .gitattributes "* -text", the root keyring given or dearmored from the checkout, no
# parent); a channel directory replaced exactly (deletions included) and nothing else
# touched; the commit message; the checkout left as it was; --base refusing a channel
# changed meanwhile and accepting another channel's change; a branch moved between fetch
# and push (non-fast-forward) applied again on the new tip with the other publication kept;
# the attempts bound; a refusal of another kind not retried.
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
script="$here/publish-apt-channel.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
says() { grep -qF -- "$1" <<<"$out"; }

export GIT_AUTHOR_NAME="bench" GIT_AUTHOR_EMAIL="bench@orkeon.invalid"
export GIT_COMMITTER_NAME="bench" GIT_COMMITTER_EMAIL="bench@orkeon.invalid"
remote="$work/remote.git"
git init -q --bare "$remote"
# The checkout the workflows run from: its own branch, origin = the bare repository.
git clone -q "$remote" "$work/checkout" 2>/dev/null
echo "source" > "$work/checkout/file.txt"
git -C "$work/checkout" add file.txt
git -C "$work/checkout" commit -q -m "main"
git -C "$work/checkout" push -q origin HEAD:refs/heads/main
checkout_head="$(git -C "$work/checkout" rev-parse HEAD)"
printf '\x99\x00\x33binary-keyring' > "$work/keyring.gpg"
printf -- '-----BEGIN PGP PUBLIC KEY BLOCK-----\n\nxx\n' > "$work/armored.gpg"

dir() { # <name> <file=content>...: a built channel directory
  local d="$work/built/$1" kv
  rm -rf "$d"; mkdir -p "$d"
  shift
  for kv in "$@"; do mkdir -p "$(dirname "$d/${kv%%=*}")"; printf '%s\n' "${kv#*=}" > "$d/${kv%%=*}"; done
  echo "$d"
}
run() { # <publish-apt-channel arguments...>: sets $out and $code
  code=0
  out="$(bash "$script" --repo-dir "${REPO:-$work/checkout}" "$@" 2>&1)" || code=$?
}
tip()     { git --git-dir="$remote" rev-parse -q --verify "refs/heads/apt" 2>/dev/null || true; }
subject() { git --git-dir="$remote" log -1 --format=%s apt; }
count()   { git --git-dir="$remote" rev-list --count apt; }
show()    { git --git-dir="$remote" show "apt:$1" 2>/dev/null; }
files()   { git --git-dir="$remote" ls-tree -r --name-only apt | tr '\n' ' '; }
other_push() { # <path> <content>: another publication lands on the remote
  local o="$work/other"
  rm -rf "$o"
  git clone -q --branch apt "$remote" "$o" 2>/dev/null
  mkdir -p "$o/$(dirname "$1")"
  printf '%s\n' "$2" > "$o/$1"
  git -C "$o" add -A
  git -C "$o" commit -q -m "apt: other $1"
  git -C "$o" push -q origin apt
  rm -rf "$o"
}

echo "# usage"
run --channel rc --message x
check "a --channel needs its --dir" test "$code" -eq 2
run --channel rc --dir "$(dir a x=1)"
check "--message is required" test "$code" -eq 2
run --channel 'R C' --dir "$(dir a x=1)" --message x
check "a channel name is checked" test "$code" -eq 2

echo "# --read before the branch exists"
run --read "$work/read0"
check "exits 0" test "$code" -eq 0
check "prints no commit" test -z "$out"
check "leaves the directory empty" test -z "$(ls -A "$work/read0")"

echo "# first use: the orphan branch"
run --channel rc --dir "$(dir rc InRelease=one)" --message v1.0.0-rc.3
check "without --root-keyring nor installers/apt in the checkout, nothing is created" test "$code" -eq 1
check "... and it says so" says "give --root-keyring"
check "... nothing pushed" test -z "$(tip)"
run --channel rc --dir "$(dir rc InRelease=one)" --message v1 --root-keyring "$work/armored.gpg"
check "an armored root keyring is refused" test "$code" -eq 1
run --channel rc --dir "$(dir rc InRelease=one Packages=p1 by-hash/SHA256/aaa=old)" --channel stable --dir "$(dir stable)" \
  --message v1.0.0-rc.3 --root-keyring "$work/keyring.gpg"
check "exits 0" test "$code" -eq 0
check "orphan: the first commit has no parent" test "$(git --git-dir="$remote" rev-list --parents -n1 apt | wc -w)" -eq 1
check "commit message names the changed channel (an empty directory is no change)" test "$(subject)" = "apt: rc v1.0.0-rc.3"
check "README.md says the branch is managed by CI" bash -c 'git --git-dir="$0" show apt:README.md | grep -q "managed by CI"' "$remote"
check ".gitattributes is '* -text'" test "$(show .gitattributes)" = "* -text"
check "the root keyring is the given binary file" bash -c 'cmp -s <(git --git-dir="$0" show apt:orkeon-archive-keyring.gpg) "$1"' "$remote" "$work/keyring.gpg"
check "the tree holds the root files and rc only" test "$(files)" = ".gitattributes README.md orkeon-archive-keyring.gpg rc/InRelease rc/Packages rc/by-hash/SHA256/aaa "
check "the author is the one asked for" test "$(git --git-dir="$remote" log -1 --format=%an apt)" = bench
check "the checkout's HEAD did not move" test "$(git -C "$work/checkout" rev-parse HEAD)" = "$checkout_head"
check "the checkout's working tree is clean" test -z "$(git -C "$work/checkout" status --porcelain)"

echo "# --read on the existing branch"
run --read "$work/read1"
check "prints the tip" test "$out" = "$(tip)"
check "exports its files" test "$(cat "$work/read1/rc/InRelease")" = one
base="$out"

echo "# another channel lands meanwhile; ours is published on top, exactly replaced"
other_push dev/InRelease "dev build"
run --channel rc --dir "$(dir rc InRelease=two Packages=p2 by-hash/SHA256/bbb=new)" --channel stable --dir "$(dir stable InRelease=s)" \
  --base "$base" --message v1.0.0
check "exits 0 (--base: only the given channels must be unchanged)" test "$code" -eq 0
check "message lists both channels in the order given" test "$(subject)" = "apt: rc,stable v1.0.0"
check "rc is replaced, deletions included" test "$(git --git-dir="$remote" ls-tree -r --name-only apt rc | tr '\n' ' ')" = "rc/InRelease rc/Packages rc/by-hash/SHA256/bbb "
check "dev/ is untouched" test "$(show dev/InRelease)" = "dev build"
check "the root keyring is kept" bash -c 'cmp -s <(git --git-dir="$0" show apt:orkeon-archive-keyring.gpg) "$1"' "$remote" "$work/keyring.gpg"
check "history is linear, the other publication kept" test "$(git --git-dir="$remote" log --format=%s apt | sed -n 2p)" = "apt: other dev/InRelease"

echo "# --base refuses a channel changed meanwhile"
run --read "$work/read2"
base="$out"
other_push rc/InRelease "someone else"
before="$(tip)"
run --channel rc --dir "$(dir rc InRelease=three)" --base "$base" --message v1.0.1
check "exits 1" test "$code" -eq 1
check "... naming the channel" says "rc/ changed on the remote since"
check "... nothing pushed" test "$(tip)" = "$before"
run --channel rc --dir "$(dir rc InRelease=three)" --base 0123456789012345678901234567890123456789 --message v
check "an unknown --base is refused" test "$code" -eq 1

echo "# nothing changed"
before="$(tip)"
run --channel dev --dir "$(dir dev InRelease='dev build')" --message again
check "exits 0" test "$code" -eq 0
check "... says so" says "nothing to publish"
check "... no commit" test "$(tip)" = "$before"

echo "# a new root keyring replaces the old one"
printf 'other-binary-keyring' > "$work/keyring2.gpg"
run --channel dev --dir "$(dir dev InRelease='dev build')" --message key --root-keyring "$work/keyring2.gpg"
check "message says root when no channel changed" test "$(subject)" = "apt: root key"
check "the root keyring is replaced" bash -c 'cmp -s <(git --git-dir="$0" show apt:orkeon-archive-keyring.gpg) "$1"' "$remote" "$work/keyring2.gpg"

echo "# the branch moves between fetch and push (non-fast-forward)"
hook="$work/checkout/.git/hooks/pre-push"
cat > "$hook" <<EOF
#!/bin/sh
n=\$(cat "$work/races" 2>/dev/null || echo 0)
[ "\$n" -lt "\$(cat "$work/race-limit")" ] || exit 0
echo \$((n + 1)) > "$work/races"
unset GIT_DIR GIT_INDEX_FILE GIT_WORK_TREE
o="$work/race-clone"; rm -rf "\$o"
git clone -q --branch apt "$remote" "\$o" 2>/dev/null
echo "dev race \$n\$(cat "$work/race-salt" 2>/dev/null)" > "\$o/dev/InRelease"
git -C "\$o" -c user.name=race -c user.email=race@orkeon.invalid commit -qam "apt: dev race \$n"
git -C "\$o" push -q origin apt
EOF
chmod +x "$hook"
echo 1 > "$work/race-limit"
run --read "$work/read3"
base="$out"
n_before="$(count)"
run --channel rc --dir "$(dir rc InRelease=four)" --base "$base" --message v1.0.2
check "exits 0" test "$code" -eq 0
check "the first push was refused and retried" says "moved meanwhile"
check "two commits more: the other publication and ours" test "$(count)" -eq $((n_before + 2))
check "ours is on top" test "$(subject)" = "apt: rc v1.0.2"
check "the other publication is kept" test "$(show dev/InRelease)" = "dev race 0"
check "ours is there" test "$(show rc/InRelease)" = four
check "never forced: the base is an ancestor" git --git-dir="$remote" merge-base --is-ancestor "$base" apt

echo "# the attempts bound"
rm -f "$work/races"; echo 99 > "$work/race-limit"; echo " again" > "$work/race-salt"
run --channel rc --dir "$(dir rc InRelease=five)" --message v1.0.3 --attempts 2
check "a branch that keeps moving exits 1" test "$code" -eq 1
check "... after the attempts given" says "gave up after 2 attempts"
check "... our commit is not there" bash -c '! git --git-dir="$0" log --format=%s apt | grep -q v1.0.3' "$remote"
rm -f "$hook"

echo "# a refusal of another kind is not retried"
printf '#!/bin/sh\necho "protected branch: pushes by this actor are refused" >&2\nexit 1\n' > "$remote/hooks/pre-receive"
chmod +x "$remote/hooks/pre-receive"
run --channel rc --dir "$(dir rc InRelease=six)" --message v1.0.4
check "exits 1" test "$code" -eq 1
check "... at once" bash -c '! grep -q "attempt 2/" <<<"$0"' "$out"
rm -f "$remote/hooks/pre-receive"

echo "# first use, the keyring taken from the checkout's installers/apt"
remote2="$work/remote2.git"
git init -q --bare "$remote2"
git clone -q "$remote2" "$work/checkout2" 2>/dev/null
mkdir -p "$work/checkout2/installers/apt"
gpg --batch --enarmor < "$work/keyring.gpg" 2>/dev/null > "$work/checkout2/installers/apt/orkeon-archive-keyring.asc"
REPO="$work/checkout2" run --channel rc --dir "$(dir rc InRelease=one)" --message v1
check "exits 0" test "$code" -eq 0
check "the root keyring is the dearmored certificate" bash -c 'cmp -s <(git --git-dir="$0" show apt:orkeon-archive-keyring.gpg) "$1"' "$remote2" "$work/keyring.gpg"

if [ "$failed" -gt 0 ]; then echo "test-publish-apt-channel: ${failed} check(s) failed"; exit 1; fi
echo "test-publish-apt-channel passed"
