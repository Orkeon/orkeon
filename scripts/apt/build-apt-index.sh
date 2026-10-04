#!/usr/bin/env bash
# Builds, merges and signs the flat apt index of one channel of the Orkeon apt repository.
#
# Usage:
#   scripts/apt/build-apt-index.sh --channel stable|rc|dev --dir <apt branch>/<channel>
#                                  --keyring <binary OpenPGP certificate>
#                                  [--deb <file.deb>=<tag>/<asset name>]...
#                                  [--yank <package>=<version>[/<arch>]]...
#                                  [--date-epoch <seconds>]
#
# Environment: APT_SIGNING_KEY (the armored secret signing subkey) and, when the key is
# protected, APT_SIGNING_PASSPHRASE. The key is imported into a temporary GNUPGHOME that
# is destroyed on exit; the passphrase only ever travels through a file descriptor.
#
# What it writes in --dir: InRelease, Release, Release.gpg, Packages, Packages.gz and
# by-hash/SHA256/<sha256>. The packages themselves are never copied: each stanza's
# Filename is releases/download/<tag>/<asset>, which apt resolves against the source
# URI (https://github.com/Orkeon/orkeon/), i.e. the Release asset itself.
#
# The rules apt imposes, all enforced here:
#   - Suite is the source path without its trailing slash (raw/apt/<channel>), and there
#     is no Codename, otherwise apt reports "Conflicting distribution" on every update;
#   - Origin and Label are fixed to Orkeon: apt asks every machine to accept a change;
#   - SHA256 only (MD5, SHA1 and SHA512 disabled, in the stanzas and in the Release):
#     by-hash follows the strongest hash listed, and only by-hash/SHA256 exists;
#   - by-hash generations are pruned here, keeping the last 3 (by-hash/SHA256/generations
#     lists them), never by apt-ftparchive, whose pruning reads mtimes a git checkout resets;
#   - Date comes from the clock and must be strictly after the published one, otherwise
#     apt keeps its cached InRelease and a yank never takes effect;
#   - a package/version/architecture already published never changes bytes: a different
#     SHA256 for the same triplet is refused. The same bytes again (the keyring package
#     attached to a later Release) keep the first Filename;
#   - an asset name never carries a "~": apt sends it as %7e and GitHub renames it.
#
# --yank removes a published triplet (every architecture when /<arch> is omitted); the
# index is then signed again with a newer Date. --date-epoch replaces the clock (tests).
# Nothing is written in --dir unless the new index verifies with gpgv (and sqv when
# installed) against --keyring. Needs apt-ftparchive (apt-utils), dpkg, gpg, gpgv, perl.
set -euo pipefail
export LC_ALL=C

KEEP_GENERATIONS=3
ARCHITECTURES="amd64 arm64"

CHANNEL=""
DIR=""
KEYRING=""
DATE_EPOCH=""
DEBS=()
YANKS=()

die()  { echo "build-apt-index: $*" >&2; exit 1; }
usage() { echo "build-apt-index: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --channel)    [[ $# -ge 2 ]] || usage "$1 needs a value"; CHANNEL="$2"; shift 2 ;;
    --dir)        [[ $# -ge 2 ]] || usage "$1 needs a value"; DIR="$2"; shift 2 ;;
    --keyring)    [[ $# -ge 2 ]] || usage "$1 needs a value"; KEYRING="$2"; shift 2 ;;
    --deb)        [[ $# -ge 2 ]] || usage "$1 needs a value"; DEBS+=("$2"); shift 2 ;;
    --yank)       [[ $# -ge 2 ]] || usage "$1 needs a value"; YANKS+=("$2"); shift 2 ;;
    --date-epoch) [[ $# -ge 2 ]] || usage "$1 needs a value"; DATE_EPOCH="$2"; shift 2 ;;
    -h|--help) sed -n '2,39p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done

case "$CHANNEL" in
  stable|rc|dev) ;;
  "") usage "--channel is required (stable, rc or dev)" ;;
  *) usage "unknown channel '$CHANNEL' (stable, rc or dev)" ;;
esac
[[ -n "$DIR" ]] || usage "--dir is required"
[[ -n "$KEYRING" ]] || usage "--keyring is required (the public certificate the index must verify with)"
[[ -z "$DATE_EPOCH" || "$DATE_EPOCH" =~ ^[0-9]+$ ]] || usage "--date-epoch takes seconds since the epoch"
[[ -n "${APT_SIGNING_KEY:-}" ]] || die "APT_SIGNING_KEY is empty: the index cannot be signed"

for tool in apt-ftparchive dpkg dpkg-deb gpg gpgv gzip sha256sum perl; do
  command -v "$tool" >/dev/null 2>&1 || die "$tool not found (apt-ftparchive comes with apt-utils)"
done

[[ -f "$KEYRING" ]] || die "keyring not found: $KEYRING"
KEYRING="$(cd "$(dirname "$KEYRING")" && pwd)/$(basename "$KEYRING")"
if head -c 64 "$KEYRING" | grep -q -- '-----BEGIN PGP'; then
  die "$KEYRING is armored: apt 2.4-2.8 answer NO_PUBKEY for an armored .gpg keyring; give the binary certificate (gpg --export)"
fi

mkdir -p "$DIR"
DIR="$(cd "$DIR" && pwd)"
SUITE="raw/apt/$CHANNEL"

WORK="$(mktemp -d)"
cleanup() {
  if [[ -d "$WORK/gnupg" ]]; then
    GNUPGHOME="$WORK/gnupg" gpgconf --kill all >/dev/null 2>&1 || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT
mkdir -p "$WORK/in" "$WORK/stage" "$WORK/gpgv-home"
chmod 700 "$WORK/gpgv-home"

# --- 1. Date: strictly after the published one ------------------------------------
NOW="${DATE_EPOCH:-$(date -u +%s)}"
DATE_RFC="$(LC_ALL=C date -u -d "@$NOW" '+%a, %d %b %Y %H:%M:%S +0000')"
published=""
for f in "$DIR/InRelease" "$DIR/Release"; do
  if [[ -f "$f" ]]; then published="$f"; break; fi
done
if [[ -n "$published" ]]; then
  prev_date="$(sed -n 's/^Date:[[:space:]]*//p' "$published" | head -n1)"
  [[ -n "$prev_date" ]] || die "$published has no Date field; refusing to guess"
  prev_epoch="$(date -u -d "$prev_date" +%s)" || die "cannot read the Date of $published: $prev_date"
  if (( NOW <= prev_epoch )); then
    die "refused: Date $DATE_RFC is not strictly after the published Date $prev_date ($published); apt would keep its cached index. Nothing written."
  fi
fi

# --- 2. One stanza per --deb, Filename pointing at its Release asset --------------
: > "$WORK/new"
i=0
for spec in ${DEBS[@]+"${DEBS[@]}"}; do
  i=$((i + 1))
  [[ "$spec" == *=* ]] || usage "--deb takes <file.deb>=<tag>/<asset>: $spec"
  file="${spec%=*}"
  dest="${spec##*=}"
  tag="${dest%%/*}"
  asset="${dest#*/}"
  [[ -f "$file" ]] || die "no such .deb: $file"
  [[ "$dest" == */* && "$asset" != */* ]] || usage "--deb destination must be <tag>/<asset>: $dest"
  [[ "$tag" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*$ ]] || die "invalid tag '$tag' in $spec"
  [[ "$asset" != *"~"* ]] || die "asset name '$asset' carries a '~': apt requests it as %7e and GitHub renames it on upload; publish it without '~'"
  [[ "$asset" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*\.deb$ ]] || die "invalid asset name '$asset' in $spec"

  pkg="$(dpkg-deb -f "$file" Package)" || die "$file is not a readable .deb"
  ver="$(dpkg-deb -f "$file" Version)"
  arch="$(dpkg-deb -f "$file" Architecture)"
  case "$arch" in
    all|amd64|arm64) ;;
    *) die "$file: architecture '$arch' is not served (amd64, arm64 or all)" ;;
  esac
  [[ "$asset" == "${pkg}_"*"_${arch}.deb" ]] || die "asset '$asset' does not name $pkg for $arch ($file)"
  if [[ "$CHANNEL" == stable && "$ver" == *"~"* ]]; then
    die "refused: $pkg $ver is a prerelease; the stable channel takes final versions only"
  fi

  mkdir "$WORK/in/$i"
  ln -s "$(cd "$(dirname "$file")" && pwd)/$(basename "$file")" "$WORK/in/$i/$asset"
  ( cd "$WORK/in/$i" && apt-ftparchive \
      -o APT::FTPArchive::MD5=false -o APT::FTPArchive::SHA1=false -o APT::FTPArchive::SHA512=false \
      packages . ) > "$WORK/stanza" || die "apt-ftparchive packages failed on $file"
  FILENAME="releases/download/$tag/$asset" perl -0pe 's/^Filename: .*$/Filename: $ENV{FILENAME}/m' \
    "$WORK/stanza" >> "$WORK/new"
done

# --- 3. Merge into the published Packages, keyed by Package/Version/Architecture --
# Same triplet, same SHA256: ignored. Same triplet, other SHA256: refused (exit 3).
# Sorted by package, then Debian version order (dpkg), then architecture.
merge_status=0
perl - "$DIR/Packages" "$WORK/new" "$WORK/stage/Packages" ${YANKS[@]+"${YANKS[@]}"} <<'PERL' || merge_status=$?
use strict;
use warnings;

my ($old, $new, $out, @yanks) = @ARGV;

sub stanzas {
  my ($file) = @_;
  return () unless -e $file;
  local $/ = "";
  open my $fh, '<', $file or die "cannot read $file: $!\n";
  my @all;
  while (my $s = <$fh>) {
    $s =~ s/\n+\z/\n/;
    push @all, $s if $s =~ /\S/;
  }
  close $fh;
  return @all;
}
sub field {
  my ($s, $name) = @_;
  return $s =~ /^\Q$name\E:[ \t]*(.*?)[ \t]*$/m ? $1 : undef;
}
sub triplet {
  my ($s, $origin) = @_;
  my @k = map { field($s, $_) } qw(Package Version Architecture);
  for (@k) { die "build-apt-index: a stanza in $origin lacks Package, Version or Architecture\n" unless defined && length }
  return @k;
}

my (%index, %added);
for my $s (stanzas($old)) {
  my $k = join ' ', triplet($s, $old);
  die "build-apt-index: $old lists $k twice\n" if exists $index{$k};
  $index{$k} = $s;
}
for my $s (stanzas($new)) {
  my $k = join ' ', triplet($s, 'the new packages');
  if (my $was = $index{$k}) {
    my ($sha_was, $sha_new) = (field($was, 'SHA256') // '', field($s, 'SHA256') // '');
    if ($sha_was ne $sha_new || (field($was, 'Size') // '') ne (field($s, 'Size') // '')) {
      print STDERR "build-apt-index: refused: $k is already published with SHA256 $sha_was"
        . " (" . (field($was, 'Filename') // '?') . "); the new file has SHA256 $sha_new."
        . " A published package never changes bytes: give it a new version. Nothing written.\n";
      exit 3;
    }
    print STDERR "unchanged  $k (" . field($was, 'Filename') . ")\n";
    next;
  }
  $index{$k} = $s;
  $added{$k} = 1;
  print STDERR "added      $k (" . field($s, 'Filename') . ")\n";
}

for my $y (@yanks) {
  my ($pkg, $ver, $arch) = $y =~ m{^([a-z0-9][a-z0-9+.-]*)=([^/=\s]+)(?:/([a-z0-9]+))?$}
    or do { print STDERR "build-apt-index: --yank takes <package>=<version>[/<arch>]: $y\n"; exit 2 };
  my @hits = grep {
    my @t = split / /, $_;
    $t[0] eq $pkg && $t[1] eq $ver && (!defined $arch || $t[2] eq $arch)
  } sort keys %index;
  unless (@hits) {
    print STDERR "build-apt-index: --yank $y matches no published stanza. Nothing written.\n";
    exit 1;
  }
  for my $k (@hits) {
    if ($added{$k}) {
      print STDERR "build-apt-index: --yank $y removes $k, which this same run adds. Nothing written.\n";
      exit 1;
    }
    print STDERR "yanked     $k (" . field($index{$k}, 'Filename') . ")\n";
    delete $index{$k};
  }
}

my %cmp_cache;
sub vercmp {
  my ($v, $w) = @_;
  return 0 if $v eq $w;
  return $cmp_cache{"$v $w"} //= do {
    system('dpkg', '--compare-versions', $v, 'lt', $w) == 0 ? -1 : 1;
  };
}
my @keys = sort {
  my @x = split / /, $a;
  my @y = split / /, $b;
  $x[0] cmp $y[0] || vercmp($x[1], $y[1]) || $x[2] cmp $y[2]
} keys %index;

open my $fh, '>', $out or die "cannot write $out: $!\n";
print {$fh} $index{$_}, "\n" for @keys;
close $fh or die "cannot write $out: $!\n";
print STDERR "stanzas    " . scalar(@keys) . "\n";
PERL
case "$merge_status" in
  0) ;;
  2) exit 2 ;;
  *) exit "$merge_status" ;;
esac

# --- 4. Packages.gz and Release ----------------------------------------------------
gzip -9n -c "$WORK/stage/Packages" > "$WORK/stage/Packages.gz"
( cd "$WORK/stage" && apt-ftparchive \
    -o APT::FTPArchive::MD5=false -o APT::FTPArchive::SHA1=false -o APT::FTPArchive::SHA512=false \
    -o APT::FTPArchive::Release::Origin=Orkeon \
    -o APT::FTPArchive::Release::Label=Orkeon \
    -o "APT::FTPArchive::Release::Suite=$SUITE" \
    -o "APT::FTPArchive::Release::Architectures=$ARCHITECTURES" \
    -o APT::FTPArchive::Release::Acquire-By-Hash=yes \
    -o "APT::FTPArchive::Release::Date=$DATE_RFC" \
    release . ) > "$WORK/Release" || die "apt-ftparchive release failed"
mv "$WORK/Release" "$WORK/stage/Release"

# The shape apt needs, checked rather than trusted to this apt-ftparchive's defaults.
release_field() { sed -n "s/^$1:[[:space:]]*//p" "$WORK/stage/Release"; }
[[ "$(release_field Origin)" == Orkeon && "$(release_field Label)" == Orkeon ]] || die "Release: Origin/Label are not Orkeon"
[[ "$(release_field Suite)" == "$SUITE" ]] || die "Release: Suite is not $SUITE"
[[ "$(release_field Architectures)" == "$ARCHITECTURES" ]] || die "Release: Architectures is not $ARCHITECTURES"
[[ "$(release_field Acquire-By-Hash)" == yes ]] || die "Release: Acquire-By-Hash is not yes"
[[ "$(release_field Date)" == "$DATE_RFC" ]] || die "Release: Date is not $DATE_RFC"
if grep -qE '^(Codename|Valid-Until|MD5Sum|SHA1|SHA512):' "$WORK/stage/Release"; then
  die "Release lists Codename, Valid-Until, MD5Sum, SHA1 or SHA512; this apt-ftparchive ignored an option"
fi
grep -qx 'SHA256:' "$WORK/stage/Release" || die "Release has no SHA256 section"

# --- 5. Sign: InRelease (clearsigned) and Release.gpg (detached, armored) ----------
export GNUPGHOME="$WORK/gnupg"
mkdir -m 700 "$GNUPGHOME"
umask 077
printf '%s\n' "$APT_SIGNING_KEY" > "$WORK/signing-key.asc"
umask 022
gpg_sign() {
  gpg --batch --quiet --yes --pinentry-mode loopback --passphrase-fd 3 "$@" \
    3<<<"${APT_SIGNING_PASSPHRASE:-}"
}
gpg_sign --import "$WORK/signing-key.asc" 2>"$WORK/gpg.log" \
  || { cat "$WORK/gpg.log" >&2; die "APT_SIGNING_KEY could not be imported"; }
rm -f "$WORK/signing-key.asc"
mapfile -t secret_keys < <(gpg --batch --with-colons --list-secret-keys | awk -F: '$1 == "sec" { getline; print $10 }')
[[ ${#secret_keys[@]} -eq 1 ]] || die "APT_SIGNING_KEY must hold exactly one key (found ${#secret_keys[@]})"
signer="${secret_keys[0]}"
gpg_sign --digest-algo SHA256 --local-user "$signer" --clearsign \
  --output "$WORK/stage/InRelease" "$WORK/stage/Release" 2>"$WORK/gpg.log" \
  || { cat "$WORK/gpg.log" >&2; die "signing InRelease failed"; }
gpg_sign --digest-algo SHA256 --local-user "$signer" --armor --detach-sign \
  --output "$WORK/stage/Release.gpg" "$WORK/stage/Release" 2>"$WORK/gpg.log" \
  || { cat "$WORK/gpg.log" >&2; die "signing Release.gpg failed"; }

# --- 6. Verify against the published certificate before anything is written -------
unverified() { cat "$WORK/verify.log" >&2; die "refused: $1 does not verify against $KEYRING. Nothing written."; }
gpgv --homedir "$WORK/gpgv-home" --keyring "$KEYRING" --output "$WORK/inrelease.txt" \
  "$WORK/stage/InRelease" >"$WORK/verify.log" 2>&1 || unverified "InRelease (gpgv)"
gpgv --homedir "$WORK/gpgv-home" --keyring "$KEYRING" \
  "$WORK/stage/Release.gpg" "$WORK/stage/Release" >"$WORK/verify.log" 2>&1 || unverified "Release.gpg (gpgv)"
cmp -s "$WORK/inrelease.txt" "$WORK/stage/Release" || die "refused: the text InRelease signs is not Release. Nothing written."
if command -v sqv >/dev/null 2>&1 && sqv --help 2>&1 | grep -q -- '--cleartext'; then
  sqv --keyring "$KEYRING" --cleartext --output "$WORK/inrelease.sqv" \
    "$WORK/stage/InRelease" >"$WORK/verify.log" 2>&1 || unverified "InRelease (sqv)"
  sqv --keyring "$KEYRING" --signature-file "$WORK/stage/Release.gpg" \
    "$WORK/stage/Release" >"$WORK/verify.log" 2>&1 || unverified "Release.gpg (sqv)"
fi

# --- 7. by-hash: this generation's files, then prune to the last generations -------
BYHASH="$DIR/by-hash/SHA256"
mkdir -p "$BYHASH"
sha_packages="$(sha256sum "$WORK/stage/Packages" | cut -d' ' -f1)"
sha_gz="$(sha256sum "$WORK/stage/Packages.gz" | cut -d' ' -f1)"
for pair in "$sha_packages:Packages" "$sha_gz:Packages.gz"; do
  [[ -f "$BYHASH/${pair%%:*}" ]] || cp "$WORK/stage/${pair#*:}" "$BYHASH/${pair%%:*}"
done
manifest="$BYHASH/generations"
touch "$manifest"
last="$(tail -n1 "$manifest" | cut -d' ' -f2-)"
if [[ "$last" != "$sha_packages $sha_gz" ]]; then
  echo "$NOW $sha_packages $sha_gz" >> "$manifest"
fi
tail -n "$KEEP_GENERATIONS" "$manifest" > "$WORK/generations"
cp "$WORK/generations" "$manifest"

# --- 8. Publish the signed files, InRelease last --------------------------------------
for f in Packages Packages.gz Release Release.gpg InRelease; do
  cp "$WORK/stage/$f" "$DIR/.$f.new"
  mv -f "$DIR/.$f.new" "$DIR/$f"
done
kept=" $(cut -d' ' -f2- "$manifest" | tr '\n' ' ') "
for f in "$BYHASH"/*; do
  name="$(basename "$f")"
  [[ "$name" == generations ]] && continue
  if [[ "$kept" != *" $name "* ]]; then
    rm -f "$f"
    echo "pruned     by-hash/SHA256/$name"
  fi
done >&2

echo "build-apt-index: $SUITE signed by $signer, Date $DATE_RFC, Packages $sha_packages" >&2
