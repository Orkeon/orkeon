> 🇫🇷 [Version française](../fr/guides/verify-what-you-install.md)

# Verify what you install

> **See also**: [Security policy](../../SECURITY.md) · [Publication matrix](../reference/publication-matrix.md) · [Back to the index](../INDEX.md)

Every released Orkeon artefact — the NuGet packages, the installers, the CLI archives, the
`.deb` packages, the MSIs, the SBOM — is built by a public GitHub Actions workflow and
**attested**: a signed statement says *this exact file was produced by this workflow, at this
commit, in this repository*. You can check that statement yourself, with tools you already
have, without trusting the maintainer, the download page, or this document.

This page lists what the chain proves, what it does not, and the commands. Each command was
run on the published `v1.0.0-rc.5` artefacts before being written down here, and the comments
under it show what it printed. For another release, change `VER`.

## Which check for which file

| You have | What to check | Section |
|---|---|---|
| An archive, a `.deb`, an MSI or the SBOM from the Releases page | its `SHA256SUMS` line, then its attestation | [Verify a release asset](#verify-a-release-asset-installer-cli-archive-deb-msi) |
| Orkeon installed with apt | the archive key, once; apt then checks every package through the signed index | [Verify the APT signing key](#verify-the-apt-signing-key) |
| A `.nupkg` from nuget.org | its repository signature, then the attestation of the bytes nuget.org received | [Verify a NuGet package](#verify-a-nuget-package) |
| A `.nupkg` of a tagged version from GitHub Packages | its attestation, directly | [Verify a NuGet package](#verify-a-nuget-package) |
| A dev build (`<version>.dev.<n>`) or the `orkeon-runners` image | nothing to check against: neither is attested | [What the chain establishes](#what-the-chain-establishes) |

## What the chain establishes

| Mechanism | Where it lives | What it proves |
|---|---|---|
| **Trusted Publishing (OIDC)** to NuGet.org | `publish.yml`, step *NuGet.org login* (`NuGet/login`) | The push used a short-lived key minted for that one workflow run. There is no long-lived NuGet API key anywhere — none can leak, none needs rotating. |
| **Build provenance attestation** (SLSA v1, Sigstore) | `publish.yml`, step *Attest the packages and the SBOM* — on every `*.nupkg` and the SBOM; `release.yml`, step *Attest the release assets* — on every archive, `.deb`, MSI and the SBOM | The SHA-256 of the file is recorded in a statement naming the workflow file, the tag, the commit and the run. It is signed with a short-lived certificate that Sigstore's public instance issued to that workflow, and entered in Sigstore's public transparency log (Rekor). A file with a different digest has no statement. |
| **Immutable Releases** | The repository's setting; `release.yml` creates the Release as a draft and publishes it once every asset is attached (step *Publish the draft Release*) | Once a Release is published, none of its assets can be replaced or deleted. GitHub signs a statement of its own about it (`https://in-toto.io/attestation/release/v0.2`): the tag and the digest of every asset, both `SHA256SUMS` manifests included. |
| **`ContinuousIntegrationBuild=true`** at pack time | `publish.yml`, step `dotnet pack` | Paths inside the PDBs and assemblies are normalised, so the packed bytes do not depend on the runner's directory layout. It is a *deterministic-build* setting, not a guarantee that you can rebuild the identical bytes yourself — the SDK, the runner image and the NuGet graph would have to match. |
| **`SHA256SUMS`** manifests | Release assets (`SHA256SUMS` for the archives, the `.deb` packages and the SBOM; `SHA256SUMS.msi` for the two MSIs) | Integrity of what you downloaded against what the workflow uploaded. Cheap, offline — but a manifest is just another release asset: what ties each file it lists to a workflow is that file's attestation. |
| **Post-publish verification** | `release-verify.yml`, called by `release.yml` right after it publishes the Release (job `verify-published`), or by hand for any tag | A fresh runner downloads the *published* assets, checks that both manifests list every published asset, and nothing else, and that every checksum holds, then walks the onboarding smoke again on both `.deb` (amd64, arm64) and the `osx-arm64` tarball, so an asset that differs from what the release smokes installed is caught. |
| **Signed APT repository** | The `apt` branch, signed in `release.yml` (job `apt-publish`) with a dedicated OpenPGP key; its fingerprint and the keyring's SHA-256 are in [SECURITY.md](../../SECURITY.md#apt-archive-signing-key) | apt checks the signature of the channel's index against that key only, then every downloaded `.deb` against the SHA-256 the index lists. You check the key once, when you add the source. See [Install with apt](./install-with-apt.md). |
| **Pinned actions and base images** | Every action a workflow `uses:` is pinned to a commit SHA (the only other `uses:` call the repository's own `release-verify.yml`); every external `FROM` image is pinned by digest | The actions and base images that ran are byte for byte the ones the repository names; a tag moved upstream changes nothing. |

Two things carry **no** attestation: the dev builds of `main` on GitHub Packages
(`<version>.dev.<n>`, see below) and the `orkeon-runners` container image on GHCR.

What the chain does **not** establish: anything about the *quality* of the code. An attestation
says that the bytes came out of `.github/workflows/publish.yml` at commit X — not that
commit X is bug-free, safe to run with your credentials, or reviewed by anyone. Read the
[threat model](../../SECURITY.md#threat-model--llm-driven-tool-execution) for that.

## Verify a release asset (installer, CLI archive, `.deb`, MSI)

Release assets are attested **as uploaded**: the file you download from the Releases
page has the digest in the statement.

```bash
VER=1.0.0-rc.5
BASE="https://github.com/Orkeon/orkeon/releases/download/v$VER"
curl -fsSL -O "$BASE/orkeon-cli-$VER-osx-arm64.tar.gz" -O "$BASE/SHA256SUMS"

# 1. integrity — the manifest travelled with the file
sha256sum --check --ignore-missing SHA256SUMS
#    orkeon-cli-1.0.0-rc.5-osx-arm64.tar.gz: OK

# 2. provenance — the signed statement about this exact digest (gh >= 2.49, logged in)
gh attestation verify "orkeon-cli-$VER-osx-arm64.tar.gz" --repo Orkeon/orkeon
```

The second command checks the statement's signature, names the workflow that built the file
(`.github/workflows/release.yml@refs/tags/v1.0.0-rc.5`) and fails on any byte difference. It
wants a logged-in `gh` (`gh auth login`), even for a public repository, and prints its report
only in a terminal: in a script, rely on its exit code.

Without `gh`, ask the public API directly — no token, no login. It answers with every
statement the repository holds for that digest, base64-encoded; `jq` decodes them:

```bash
D=$(sha256sum "orkeon-cli-$VER-osx-arm64.tar.gz" | cut -d' ' -f1)
curl -fsSL "https://api.github.com/repos/Orkeon/orkeon/attestations/sha256:$D" |
  jq -r '.attestations[].bundle.dsseEnvelope.payload | @base64d | fromjson
         | "\(.predicateType)  \(.subject | length) subjects",
           (.predicate.runDetails.builder.id // empty),
           (.predicate.buildDefinition.resolvedDependencies[0].digest.gitCommit // empty)'
#    https://in-toto.io/attestation/release/v0.2  17 subjects
#    https://slsa.dev/provenance/v1  14 subjects
#    https://github.com/Orkeon/orkeon/.github/workflows/release.yml@refs/tags/v1.0.0-rc.5
#    e7dba7a43ee6d4460bc86f8f6d76cedf3e36dc72
```

No line — curl reports a 404, or the list is empty — means *no statement for these bytes*.
The API shows what GitHub holds; checking the signature on it is what `gh` adds.

A release asset has two statements:

- The **`slsa.dev/provenance/v1`** line is the workflow's statement; the two lines after it
  are its builder — the workflow file and the tag it ran on — and the commit it built. Its
  fourteen subjects are every file the run attached to the Release: the five installer
  archives, the three CLI archives, both `.deb` packages, the apt keyring package, both MSIs
  and the SBOM. The two `SHA256SUMS` manifests are the only assets missing, while every file
  they list is there.
- The **`in-toto.io/attestation/release/v0.2`** line is GitHub's own statement about the
  published Release, signed by GitHub rather than by the workflow: Orkeon Releases are
  immutable. Its seventeen subjects are the tag (`pkg:github/Orkeon/orkeon@v1.0.0-rc.5`,
  pinned to the SHA-1 of its tag object), the fourteen files above and both manifests.

The first says *which run built these bytes*; the second, *these are the bytes this tag
published, and they cannot change*.

## Verify the APT signing key

The repository's key is checked once, when you add the source: the installation block of
[Install with apt](./install-with-apt.md) compares the downloaded keyring with the SHA-256
published in [SECURITY.md](../../SECURITY.md#apt-archive-signing-key) and stops on any
difference. To check the fingerprint as well (`gnupg` installed):

```bash
sha256sum /usr/share/keyrings/orkeon-archive-keyring.gpg          # = the keyring SHA-256 in SECURITY.md
gpg --show-keys /usr/share/keyrings/orkeon-archive-keyring.gpg    # = the fingerprint in SECURITY.md
```

The output must show an ed25519 primary key with the published fingerprint, the user id
`Orkeon Archive Signing Key <arion@orkeon.org>`, and a signing subkey `[S]` that has not
expired. The armoured source of the same key is
[`installers/apt/orkeon-archive-keyring.asc`](https://github.com/Orkeon/orkeon/blob/main/installers/apt/orkeon-archive-keyring.asc);
`gpg --dearmor` turns it into the exact bytes of the keyring. After that, apt checks every
update by itself. The packages it installs are the Release assets — the keyring package,
`orkeon-archive-keyring`, among them — so the attestation commands above apply to them
unchanged. Once installed, the Debian package can be checked file by file: `dpkg -V orkeon`
prints nothing when every installed file matches the checksum the package recorded. A
minimised image — Ubuntu's Docker images — tells dpkg not to unpack `/usr/share/doc`
(`path-exclude` in `/etc/dpkg/dpkg.cfg.d/excludes`): there, the notices and licences under
`/usr/share/doc/orkeon/` print as `missing`, and only those lines are expected.

## Verify a NuGet package

Two things are true at once about a package on nuget.org, and the second surprises people:

1. **nuget.org repository-signs every package it accepts.** `dotnet nuget verify --all`
   checks that signature — it proves the bytes were not altered *after* nuget.org received
   them. Orkeon packages carry no author signature; the repository signature is the only one.
2. **Repository signing changes the file.** nuget.org appends a `.signature.p7s` entry
   and rewrites the zip directory, so the digest of the package you download is **not** the
   digest GitHub attested — the attestation covers the bytes `dotnet pack` produced, before
   the push. `gh attestation verify` on the downloaded `.nupkg` therefore fails, and that
   failure is not evidence of tampering.

The signature is always appended last, so the original bytes are recoverable exactly —
no re-zipping — with a standard-library script from this repository:

```bash
# From the root of a clone, for scripts/nupkg-unsign.py:
#    git clone --depth 1 https://github.com/Orkeon/orkeon && cd orkeon
VER=1.0.0-rc.5
curl -fsSL -o "Orkeon.$VER.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/orkeon/$VER/orkeon.$VER.nupkg"

# 1. the nuget.org repository signature — intact since nuget.org stored it
dotnet nuget verify --all "Orkeon.$VER.nupkg"
#    Signature type: Repository — CN=NuGet.org Repository by Microsoft …

# 2. the bytes as attested — strip the appended repository signature
python3 scripts/nupkg-unsign.py "Orkeon.$VER.nupkg"
#    Orkeon.1.0.0-rc.5.unsigned.nupkg  sha256:a6c81a0c77a451e0af8408d5a7774c4c8527bcfbae213f2d4c005550701548e8

# 3. provenance of those bytes
gh attestation verify "Orkeon.$VER.unsigned.nupkg" --repo Orkeon/orkeon
```

The script keeps everything before the signature's local header, drops the signature's
central-directory record, and writes an end-of-central-directory record with the counts,
size and offset put back. It refuses to guess: if the signature is not the last entry of the
archive it stops rather than emit something that would fail verification anyway.

Measured on `Orkeon.1.0.0-rc.5.nupkg`, the recovered digest has one statement (the `jq`
filter above prints it too): builder `.github/workflows/publish.yml@refs/tags/v1.0.0-rc.5`,
the commit the release assets were built from, twelve subjects — the nine packages pushed to
nuget.org at that tag, the two that only reach GitHub Packages (`Orkeon.ConsoleApp`,
`Orkeon.Generators`), and the SBOM. The attestation is literal: it lists what the workflow
built, not what reached nuget.org.

A tagged version downloaded from **GitHub Packages** is not repository-signed and verifies
directly, without the strip. The dev builds on that feed (`<version>.dev.<n>`, see the
[publication matrix](../reference/publication-matrix.md)) are not releases and carry no
attestation: there is nothing to verify them against.

## Verify the software bill of materials

Since `v1.0.0-rc.4`, every release ships a CycloneDX SBOM next to its assets —
`orkeon-<version>.sbom.cdx.json`, the NuGet dependency graph of `Orkeon.sln`, transitive
packages included and test projects excluded. A pinned `CycloneDX` dotnet tool (not a
third-party action) generates it on the runner that packaged the archives, right after them;
it is listed in `SHA256SUMS` and attested in the same step as the archives, so one statement
covers the binaries and the dependency graph they were built from. Verify it like any other
asset:

```bash
VER=1.0.0-rc.5
curl -fsSL -O "https://github.com/Orkeon/orkeon/releases/download/v$VER/orkeon-$VER.sbom.cdx.json"
gh attestation verify "orkeon-$VER.sbom.cdx.json" --repo Orkeon/orkeon
```

The `v1.0.0-rc.5` file lists 276 components under CycloneDX spec 1.7. It is a dependency
graph, not a vulnerability scan: feed it to the scanner you already use. What the attestation
adds is that the graph you scan is the graph the release was built from, not one regenerated
afterwards.

`publish.yml` runs the same tool right after the pack and attests that second file with the
packages — its own digest, in its own statement, kept as a workflow-run artefact (`sbom`)
because nuget.org has no slot for an SBOM. The copy to scan is the Release's.

## What to do when a check fails

- `sha256sum` mismatch → the download is incomplete or altered; download again from the
  Releases page, never from a mirror.
- `gh attestation verify` exits with code 4 and asks for `gh auth login` → not a failed
  check: `gh` wants a login even for a public repository. Log in, or ask the API (above).
- `gh attestation verify` fails, or the API has no statement, for a **release asset** whose
  `SHA256SUMS` line matched → the manifest and the file agree with each other but not with
  any workflow run. Do not install; report it through the [security policy](../../SECURITY.md).
- `gh attestation verify` fails on a `.nupkg` **downloaded from nuget.org** → expected;
  strip the repository signature first (above). If it still fails after the strip, report it.
- The provenance statement names another tag than the version you meant to install → the
  file is a genuine build of that other release, not the one its name claims; download the
  version you want from its own Release.
