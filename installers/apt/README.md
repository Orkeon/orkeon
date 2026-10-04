# The apt repository's signing key

This directory holds the public side of the key that signs the index of the Orkeon apt
repository:

| File | What it is |
|---|---|
| `orkeon-archive-keyring.asc` | The public certificate, ASCII-armored: the only form kept in git, readable in a diff review. |
| `keyring.version` | The version of the `orkeon-archive-keyring` package, a date `YYYY.MM.DD`. |

`scripts/package-keyring-deb.sh` turns them into `orkeon-archive-keyring_<version>_all.deb`,
which installs the binary keyring apt reads, `/usr/share/keyrings/orkeon-archive-keyring.gpg`
(produced by `gpg --dearmor`, which only decodes the base64: the same certificate always gives
the same bytes). The build is reproducible; a given version is built once, and its published
bytes are reused afterwards, never rebuilt. `scripts/apt/check-signing-key.sh` guards the
certificate in CI.

> [!WARNING]
> **The committed certificate is a development placeholder.** It was made from a throwaway
> key, whose secret part was destroyed: nothing can sign with it. Its armor carries the header
> line `Comment: PLACEHOLDER - throwaway key, ...`, which is how the guard tells it apart:
> `scripts/apt/check-signing-key.sh` prints a notice on it, and fails on it with
> `--require-real`. It must be replaced by the output of the key ceremony below before any
> apt index is published.

## The key

- OpenPGP **v4** (apt's `gpgv` reads no v6 key, Sequoia's `sqv` no v5).
- Primary key: ed25519, **certification only** (`[C]`), no expiry. It never leaves the offline
  machine, and never reaches CI.
- Signing subkey: ed25519 (`[S]`), valid **2 years**, extended at least 6 months before it
  ends. The CI guard fails when the farthest signing expiry is less than 180 days away: an
  expired subkey breaks `apt update` for every user, with no fallback.
- User ID: `Orkeon Archive Signing Key <arion@orkeon.org>`.
- Signatures use SHA-256.
- Only the signing subkey goes to GitHub, in the `apt-signing` environment. A leak is then
  answered by revoking that subkey, without asking every user to trust a new key.

## The key ceremony

On an offline machine (or a live system) with GnuPG 2.2 or later; about 20 minutes. Run the
steps in order, in one shell.

```bash
export GNUPGHOME="$(mktemp -d)"; chmod 700 "$GNUPGHOME"
printf 'personal-digest-preferences SHA256\ncert-digest-algo SHA256\n' > "$GNUPGHOME/gpg.conf"

# 1. Primary key: ed25519, certification only, no expiry. Choose a strong passphrase.
gpg --quick-generate-key 'Orkeon Archive Signing Key <arion@orkeon.org>' ed25519 cert never
FPR=$(gpg --list-keys --with-colons 'Orkeon Archive Signing Key' | awk -F: '/^fpr:/ {print $10; exit}')

# 2. Signing subkey: ed25519, 2 years.
gpg --quick-add-key "$FPR" ed25519 sign 2y

# 3. A v4 key is mandatory: this line must read "version 4", and $FPR be 40 hex digits.
gpg --export "$FPR" | gpg --list-packets | grep -m1 -A1 '^:public key packet:'
echo "${#FPR}"

# 4. Offline backups: the primary secret key and the revocation certificate
#    (GnuPG wrote the latter at step 1).
gpg --armor --export-secret-keys "$FPR" > orkeon-archive-primary-secret.asc
cp "$GNUPGHOME/openpgp-revocs.d/$FPR.rev" orkeon-archive-revocation.rev

# 5. What goes to GitHub: the signing subkey alone (the primary is exported as a stub
#    without its secret part), under a passphrase of its own, used by CI only.
gpg --armor --export-secret-subkeys "$FPR" > subkey.asc
export CI_HOME="$(mktemp -d)"; chmod 700 "$CI_HOME"
gpg --homedir "$CI_HOME" --import subkey.asc
gpg --homedir "$CI_HOME" --passwd "$FPR"        # the new passphrase is APT_SIGNING_PASSPHRASE
gpg --homedir "$CI_HOME" --armor --export-secret-subkeys "$FPR" > orkeon-archive-signing-subkey.asc
shred -u subkey.asc
# Check: the primary must show "sec#" (secret part absent), the subkey "ssb".
gpg --homedir "$CI_HOME" --list-secret-keys

# 6. The public certificate (for this directory) and the fingerprint (for SECURITY.md).
gpg --armor --export "$FPR" > orkeon-archive-keyring.asc
gpg --fingerprint "$FPR"
gpg --dearmor < orkeon-archive-keyring.asc | sha256sum
```

Where each piece goes:

| Piece | Where |
|---|---|
| `orkeon-archive-primary-secret.asc` and the primary passphrase | Offline, on two separate encrypted media. |
| `orkeon-archive-revocation.rev` | Offline, apart from the primary key. |
| `orkeon-archive-signing-subkey.asc` | The `APT_SIGNING_KEY` secret of the `apt-signing` GitHub environment (paste the whole armored block), then `shred -u` it. |
| The subkey passphrase chosen at step 5 | The `APT_SIGNING_PASSPHRASE` secret of the same environment. |
| `orkeon-archive-keyring.asc` | Replaces the placeholder in this directory, committed; set `keyring.version` to the ceremony date. |
| The fingerprint and the keyring SHA-256 (step 6) | `SECURITY.md` and `SECURITY.fr.md`, in the marker lines below. |
| The subkey expiry date | A calendar reminder, 6 months before it. |

The `apt-signing` environment and the other repository settings are listed in
[Repository settings](#repository-settings-before-the-first-publishing-tag). Then destroy both temporary homes (`rm -rf "$GNUPGHOME" "$CI_HOME"`) once the backups are
checked, and verify the committed certificate: `bash scripts/apt/check-signing-key.sh
--require-real` must pass.

## Where the fingerprint is published

The guard compares the certificate with every place that cites it: `SECURITY.md`,
`SECURITY.fr.md`, `docs/guides/install-with-apt.md` and `docs/fr/guides/install-with-apt.md`.
Each one cites it in these two lines, anywhere in a line (typically in a `text` code block;
spaces inside the values and their case do not matter):

```text
orkeon-archive-keyring fingerprint: XXXX XXXX XXXX XXXX XXXX  XXXX XXXX XXXX XXXX XXXX
orkeon-archive-keyring.gpg sha256: <64 hex digits>
```

The first is the primary key fingerprint (`gpg --fingerprint`), the second the SHA-256 of the
binary keyring (`gpg --dearmor < orkeon-archive-keyring.asc | sha256sum`), which is also
the file the package installs. Every marker present must match. Once the placeholder is
replaced, `SECURITY.md` and `SECURITY.fr.md` must carry both.

## Extending or rotating the signing subkey

At least 6 months before the subkey ends:

1. Offline, with the primary key imported: extend the subkey,
   `gpg --quick-set-expire "$FPR" 2y <subkey-fingerprint>`, or, to rotate, add a new one,
   `gpg --quick-add-key "$FPR" ed25519 sign 2y`.
2. Export the public certificate again (`gpg --armor --export "$FPR"`), replace
   `orkeon-archive-keyring.asc`, set `keyring.version` to the day's date, and update the
   SHA-256 marker lines (the fingerprint does not change). The next release publishes the new
   keyring package, which users receive with `apt upgrade`.
3. Rotation only, **one release later**: replace `APT_SIGNING_KEY` with the new subkey,
   exported alone (`gpg --armor --export-secret-subkeys '<new-subkey-fingerprint>!'`, then
   the passphrase change of step 5). The old subkey then expires, or is revoked.

A subkey that has expired cannot be repaired from the CI side: the index it signed is invalid
for every user until the new keyring reaches them.

## Repository settings before the first publishing tag

The publication runs in GitHub Actions and needs these settings, made by a maintainer in the
repository settings:

1. **Environment `apt-signing`** (Settings → Environments):
   - deployment branches and tags: the tag pattern `v*` (the `apt-publish` job of
     `release.yml` and `apt-maintenance.yml`) **and** the branch `main` (the `dev` channel's
     workflow publishes from `main`);
   - a required reviewer is recommended: every publication then waits for an approval;
   - secrets `APT_SIGNING_KEY` and `APT_SIGNING_PASSPHRASE`, from the key ceremony above.
2. **Ruleset on the tags `v*`**: no deletion, no update (a tag never moves), creation
   restricted to the maintainers.
3. **Ruleset on the branch `apt`**: no deletion, no force-push. Updates restricted to the
   publishing workflows: GitHub Actions as a bypass actor if the ruleset offers it; otherwise a
   deploy key with write access, stored in the `apt-signing` environment, and the workflows'
   checkout given that key. Check that a manual push to `apt` is refused.
4. **Immutable Releases** (Settings → General → Releases), once the `apt-dev` prerelease of
   the dev channel exists: the dev channel replaces assets on that one prerelease, which an
   immutable Release would forbid, so turn it on only after checking how the setting treats
   it. `release.yml` already creates every Release as a draft and publishes it once all its
   assets are attached, which immutable Releases require.

What runs where:

| Workflow | Publishes | Concurrency group |
|---|---|---|
| `release.yml`, job `apt-publish` (after `release`, tags only) | `rc`, and `stable` for a tag without `-` | `apt-release` |
| `apt-maintenance.yml` (`workflow_dispatch` from a tag: `resign`, `yank`, `seed`) | `stable` and `rc` | `apt-release` |
| `apt-dev.yml` (pushes to `main`) | `dev` | its own |

Every push to `apt` goes through `scripts/apt/publish-apt-channel.sh`, which replaces only its
channel directories and retries on top of a branch that moved meanwhile, so the two groups
never overwrite each other. The first publication creates the `apt` branch.

**Order for the first publication.** Run the ceremony and merge its certificate first: a tag
pushed while the certificate is the placeholder attaches no keyring package and its
`apt-publish` job fails on purpose. Then seed the Releases published before the repository
existed (`apt-maintenance.yml`, mode `seed`, targets `v1.0.0-rc.3 v1.0.0-rc.4`) **after** the
first publishing tag: that tag brings the `orkeon-archive-keyring` package, without which the
documented installation block cannot install it.
