> 🇫🇷 [Version française](../fr/guides/install-with-apt.md)

# Install with apt (Debian / Ubuntu)

> **See also**: [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) · [Verify what you install](./verify-what-you-install.md) · [Security policy](../../SECURITY.md) · [Publication matrix](../reference/publication-matrix.md) · [Back to the index](../INDEX.md)

Add the Orkeon source to apt **once**, then manage Orkeon like any other package:
`apt install orkeon`, `apt update`, `apt upgrade`. No `.deb` to download again at every
version.

The repository serves two packages:

| Package | Contents |
|---|---|
| `orkeon` | the `orkeon` CLI at `/usr/bin/orkeon` and the two **Orkeon Studio** terminal apps (`orkeon-studio-config`, `orkeon-studio-run`) — self-contained, it never pulls a `dotnet-runtime` package or a Microsoft repository |
| `orkeon-archive-keyring` | the public key apt checks the repository with, at `/usr/share/keyrings/orkeon-archive-keyring.gpg` — so a new signing key reaches you through `apt upgrade` |

It is built for **Debian 12 and 13** and **Ubuntu 22.04, 24.04 and 26.04**, on **amd64** and
**arm64**. Derivatives (Linux Mint, Pop!\_OS, Ubuntu under WSL) follow their Ubuntu or Debian
base. Ubuntu 20.04 and older are not supported: the package needs `libc6 (>= 2.34)`.

Nothing in Orkeon adds this source for you — neither the `.deb` nor the `install.sh` of the
archives: you run the commands below yourself.

## Set it up

Copy the block below into a terminal. It works from a bare Debian or Ubuntu image, under a
user that can `sudo`:

<!-- apt-setup:begin -->
```bash
sudo apt-get update && sudo apt-get install -y ca-certificates curl
curl -fsSL -o /tmp/orkeon-archive-keyring.gpg \
  https://github.com/Orkeon/orkeon/raw/apt/orkeon-archive-keyring.gpg
echo "<PENDING-KEY-CEREMONY>  /tmp/orkeon-archive-keyring.gpg" | sha256sum --check
sudo install -m 0644 /tmp/orkeon-archive-keyring.gpg /usr/share/keyrings/orkeon-archive-keyring.gpg
sudo tee /etc/apt/sources.list.d/orkeon.sources > /dev/null <<'EOF'
Types: deb
URIs: https://github.com/Orkeon/orkeon/
Suites: raw/apt/stable/
Include: orkeon orkeon-archive-keyring
Signed-By: /usr/share/keyrings/orkeon-archive-keyring.gpg
EOF
sudo apt-get update && sudo apt-get install -y orkeon orkeon-archive-keyring
```
<!-- apt-setup:end -->

Then, as with every other channel:

```bash
orkeon init          # writes ~/.config/Orkeon/appsettings.json
orkeon run crew.yaml
```

Why each line is there:

- **`ca-certificates` and `curl`** are missing from the base images; apt needs the first to
  reach GitHub over HTTPS.
- **The key is checked before it is trusted.** The first download of a key is trust on first
  use: `sha256sum --check` compares it with the SHA-256 published in the
  [security policy](../../SECURITY.md#apt-archive-signing-key) on `main`, and stops the block
  on any difference.
- **The key is a binary file in mode 0644.** apt checks signatures as the unprivileged `_apt`
  user, and an ASCII-armoured file saved under a `.gpg` name fails with `NO_PUBKEY` on apt
  2.4 to 2.8.
- **The source is a deb822 `.sources` file** with a `Signed-By:` line: the key is trusted for
  this source only, never system-wide. No `apt-key`, nothing under `/etc/apt/trusted.gpg.d`.
  `Include:` restricts the source to the two Orkeon packages on apt 3.1 and later; older apt
  ignores the field.
- **`orkeon-archive-keyring` is installed explicitly.** `orkeon` only *recommends* it, and
  `--no-install-recommends` — common in Dockerfiles — would skip it. Once installed, the
  package owns the key file you placed by hand and keeps it current.

The same source file is kept in the repository as
[`installers/apt/orkeon.sources`](https://github.com/Orkeon/orkeon/blob/main/installers/apt/orkeon.sources).

## Everyday use

| You want to… | Command |
|---|---|
| Install | `sudo apt install orkeon orkeon-archive-keyring` |
| Update | `sudo apt update && sudo apt upgrade` |
| See the versions the channel offers | `apt list -a orkeon` (or `apt-cache policy orkeon`) |
| Go back to an earlier version | `sudo apt install orkeon=<version>` |
| Stay on a version | `sudo apt-mark hold orkeon` (`unhold` to follow the channel again) |
| Check what runs | `orkeon --version` |

**Version strings.** apt writes the pre-release separator as `~`, which sorts a pre-release
*before* its final version: the release tagged `v1.0.0-rc.4` is the package version
`1.0.0~rc.4`, so going back to it is `sudo apt install orkeon=1.0.0~rc.4`. apt asks you to
confirm a downgrade; in a script, `sudo apt-get install -y --allow-downgrades orkeon=<version>`.

Every version still attached to a GitHub Release stays in its channel, so going back always
has somewhere to go. A version withdrawn for a defect disappears from the channel at the next
`apt update` (see [Withdrawn versions](#withdrawn-versions)).

Your settings are not part of the package: `~/.config/Orkeon/appsettings.json`, written by
`orkeon init`, survives updates, downgrades and removal.

## Channels

| Channel | `Suites:` line | Carries |
|---|---|---|
| `stable` | `raw/apt/stable/` | final versions only (a tag without a pre-release segment, such as `v1.0.0`) |
| `rc` | `raw/apt/rc/` | every tagged release — the release candidates **and** the final versions |
| `dev` | `raw/apt/dev/` | builds of `main` between two releases — see [Follow `main`](#follow-main-dev-builds) |

`stable` stays empty until a final version is published: a machine on `stable` installs
nothing until then. To try a release candidate, use `rc` — every final version reaches `rc`
too, so a machine on `rc` never misses one.

**Switch channel** by editing the `Suites:` line, then update:

```bash
sudo sed -i 's|^Suites: .*|Suites: raw/apt/rc/|' /etc/apt/sources.list.d/orkeon.sources
sudo apt update && sudo apt upgrade
```

Moving to a channel with newer versions is an ordinary upgrade. Moving back to a channel whose
newest version is *older* than the one installed (from `dev` to `rc`, from `rc` to `stable`)
upgrades nothing: apt keeps the installed version until the channel offers a newer one. To
go back at once, install a version of the new channel explicitly:
`sudo apt install orkeon=<version>`.

Edit only the channel name on that line: the rest of the path must match what the
repository's index declares, or apt warns "Conflicting distribution" at every update.

## Follow `main` (dev builds)

The `dev` channel carries the `orkeon` package built from every commit of `main` that CI
validated, between two releases:

```text
Suites: raw/apt/dev/
```

- **A build appears a few minutes after CI passes** on a push to `main`. Its packages are
  attached to the `apt-dev` prerelease of the repository — one fixed tag that never moves, and
  not a release.
- **Versions** look like `1.0.0~rc.4.dev.<n>`, `n` being the number of the CI run that validated
  the commit, so it grows at every build: above the
  release they start from, below the next one — so a dev machine moves to the next release
  candidate by a plain `apt upgrade` when it is published. After a final version, the dev
  builds move to the next patch (`1.0.1~dev.<n>` after `1.0.0`), as the
  [NuGet dev channel](../reference/publication-matrix.md#dev-channel-the-latest-main-between-two-tags)
  does.
- **Only the three latest builds are kept.** An older one leaves the channel; `apt install
  orkeon=<version>` can only go back that far.
- **Unstable and unsupported.** A dev build is not a release: it can break between two
  updates, and bugs found on one are fixed on `main`, never on the build. Pin a released
  version (`rc` or `stable`) for anything that must keep working.
- **apt only.** Dev builds are never published to NuGet.org; the `orkeon` dotnet tool follows
  `main` through its own channel on GitHub Packages, described in
  [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md#follow-main-the-dev-channel).

The source file, the key and the commands are those of the other channels — the `dev` channel
carries `orkeon-archive-keyring` too; only the `Suites:` line differs.

## Remove

In this order — the source first, then the packages:

```bash
sudo rm /etc/apt/sources.list.d/orkeon.sources
sudo apt purge orkeon orkeon-archive-keyring
sudo apt update
```

Purging the keyring while the source is still declared makes every later `apt update` fail on
a missing key. Your `~/.config/Orkeon` stays where it is; delete it yourself if you want it
gone.

## What apt checks, and what you check once

**apt checks, at every update and install, without you:**

- the signature of the channel's index (`InRelease`), against the key in
  `/usr/share/keyrings/orkeon-archive-keyring.gpg` and that key only;
- the SHA-256 of every package it downloads, against the index — a package that differs by a
  single byte is refused ("Hash Sum mismatch").

**You check once, when you add the source:**

- the SHA-256 of the key file — the `sha256sum --check` line of the block does it;
- optionally, the key's fingerprint, published in the
  [security policy](../../SECURITY.md#apt-archive-signing-key):

```text
Fingerprint: <PENDING-KEY-CEREMONY>
Keyring SHA-256: <PENDING-KEY-CEREMONY>
```

Until the key is created, these values — and the SHA-256 in the block above — are
placeholders, so the block's check fails on purpose; the exact lines will be published in the
security policy once the key exists.

```bash
gpg --show-keys /usr/share/keyrings/orkeon-archive-keyring.gpg   # needs the gnupg package
```

The primary key line must show that fingerprint and the user id
`Orkeon Archive Signing Key <arion@orkeon.org>`.

The packages themselves are the assets of the GitHub Releases, so each one also carries the
build provenance attestation and the `SHA256SUMS` line described in
[Verify what you install](./verify-what-you-install.md).

## Docker images

A Dockerfile installs from the repository like a machine does, with two cautions:

- **Rate limits.** The index is served by `raw.githubusercontent.com`, which limits anonymous
  traffic and may answer `429`. `apt update` turns that into a warning and keeps the index it
  had — but a fresh image has none, so the next `apt-get install` fails. Retry, and keep the
  key out of the build's downloads.
- **`--no-install-recommends`** skips `orkeon-archive-keyring` unless you name it — keep it in
  the list.

Verify the key once on your machine with the block above, put `orkeon-archive-keyring.gpg` and
[`orkeon.sources`](https://github.com/Orkeon/orkeon/blob/main/installers/apt/orkeon.sources)
next to the Dockerfile, then:

```dockerfile
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates
COPY orkeon-archive-keyring.gpg /usr/share/keyrings/orkeon-archive-keyring.gpg
COPY orkeon.sources /etc/apt/sources.list.d/orkeon.sources
RUN chmod 0644 /usr/share/keyrings/orkeon-archive-keyring.gpg /etc/apt/sources.list.d/orkeon.sources \
 && apt-get -o Acquire::Retries=5 update \
 && apt-get install -y --no-install-recommends orkeon orkeon-archive-keyring \
 && rm -rf /var/lib/apt/lists/*
```

`ca-certificates` comes first: without it, apt cannot reach the Orkeon source over HTTPS.

## Withdrawn versions

A version found defective after its release can be **withdrawn** from a channel: the index is
signed again without it, and your next `apt update` no longer offers it. An installed copy
stays installed — `apt upgrade` moves it to the newest remaining version, or
`sudo apt install orkeon=<version>` to a chosen one. A published package is never replaced
by other bytes under the same version: a fix always comes as a new version.

## Troubleshooting

| Message | Cause | Fix |
|---|---|---|
| `NO_PUBKEY` | The key file is missing, or is an ASCII-armoured file saved as `.gpg` | Run the block again: it installs the binary key in mode 0644 |
| `EXPKEYSIG` | The key on the machine is outdated — the repository is signed with a newer one | `sudo apt install orkeon-archive-keyring` if apt still lets you, otherwise download and check the key again with the block's lines up to `sudo install` |
| `Conflicting distribution` | The `Suites:` line was edited beyond the channel name | Restore the line from the [channel table](#channels), trailing `/` included |
| `429 Too Many Requests` | GitHub's limit on anonymous traffic | Retry later; in a Docker build, retry the step (`-o Acquire::Retries=5`) |
| `Hash Sum mismatch` right after a release | GitHub's cache still served the previous index for a few minutes | Wait a few minutes, then `sudo apt update` again |
| Signature errors after a key revocation was announced | The signing key was revoked | Follow the instructions published in the [security policy](../../SECURITY.md#apt-archive-signing-key): this is the only case where you must download a new key yourself |

## Without the repository

A machine that cannot reach GitHub's raw content can still install a single version: download
`orkeon_<version>_amd64.deb` (or `_arm64.deb`) and `SHA256SUMS` from the
[Releases](https://github.com/Orkeon/orkeon/releases), where `<version>` is the tag's version
(`1.0.0-rc.4`), then:

```bash
grep " orkeon_<version>_amd64.deb$" SHA256SUMS | sha256sum --check   # expect: OK
sudo apt install ./orkeon_<version>_amd64.deb
```

Such an install receives no updates: apt only upgrades from a source it knows.
