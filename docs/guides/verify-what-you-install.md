> 🇫🇷 [Version française](../fr/guides/verify-what-you-install.md)

# Verify what you install

> **See also**: [Security policy](../../SECURITY.md) · [Publication matrix](../reference/publication-matrix.md) · [Back to the index](../INDEX.md)

Every released Orkeon artefact — the NuGet packages, the installers, the CLI archives, the
`.deb`, the MSIs, the SBOM — is built by a public GitHub Actions workflow and **attested**: GitHub
signs a statement saying *this exact file was produced by this workflow, at this commit,
in this repository*. You can check that statement yourself, with tools you already
have, without trusting the maintainer, the download page, or this document.

This page lists what the chain proves, what it does not, and the commands — each one
was run on the `v1.0.0-rc.3` artefacts before being written down here.

## What the chain establishes

| Mechanism | Where it lives | What it proves |
|---|---|---|
| **Trusted Publishing (OIDC)** to NuGet.org | `publish.yml`, step *NuGet.org login* (`NuGet/login`) | The push used a short-lived key minted for that one workflow run. There is no long-lived NuGet API key anywhere — none can leak, none needs rotating. |
| **Build provenance attestation** (SLSA v1, Sigstore) | `publish.yml`, step *Attest the packages and the SBOM* — on every `*.nupkg`; `release.yml`, step *Attest the release assets* — on every archive, `.deb`, MSI and the SBOM | The SHA-256 of the file is recorded in a statement signed by GitHub's Sigstore instance, naming the workflow file, the tag, the commit and the run. A file with a different digest has no statement. |
| **`ContinuousIntegrationBuild=true`** at pack time | `publish.yml`, step `dotnet pack` | Paths inside the PDBs and assemblies are normalised, so the packed bytes do not depend on the runner's directory layout. It is a *deterministic-build* setting, not a guarantee that you can rebuild the identical bytes yourself — the SDK, the runner image and the NuGet graph would have to match. |
| **`SHA256SUMS`** manifests | Release assets (`SHA256SUMS` for the archives, the `.deb` and the SBOM; `SHA256SUMS.msi` for the two MSIs) | Integrity of what you downloaded against what the workflow uploaded. Cheap, offline, but the manifests are just release assets and are not attested themselves: the attestation of each file they list is what ties that file to the workflow. |
| **Post-publish verification** | `release-verify.yml`, called by `release.yml` once the Release is published | A fresh runner downloads the *published* assets, checks every one of them against the two manifests — an asset without a line, or a line without an asset, fails it — and walks the onboarding smoke again on the `.deb` and the `osx-arm64` tarball, so an asset that differs from what the release smokes installed is caught. |
| **Signed APT repository** | The `apt` branch, signed in `release.yml` (job `apt-publish`) with a dedicated OpenPGP key; its fingerprint and the keyring's SHA-256 are in [SECURITY.md](../../SECURITY.md#apt-archive-signing-key) | apt checks the signature of the channel's index against that key only, then every downloaded `.deb` against the SHA-256 the index lists. You check the key once, when you add the source. See [Install with apt](./install-with-apt.md). |
| **Pinned actions and base images** | Every `uses:` is a commit SHA; every external `FROM` image is pinned by digest | What ran on the runner is what the repository says ran. |

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
VER=1.0.0-rc.3
BASE="https://github.com/Orkeon/orkeon/releases/download/v$VER"
curl -fsSL -O "$BASE/orkeon-cli-$VER-osx-arm64.tar.gz" -O "$BASE/SHA256SUMS"

# 1. integrity — the manifest travelled with the file
sha256sum --check --ignore-missing SHA256SUMS
#    orkeon-cli-1.0.0-rc.3-osx-arm64.tar.gz: OK

# 2. provenance — GitHub's signed statement about this exact digest (gh >= 2.49)
gh attestation verify "orkeon-cli-$VER-osx-arm64.tar.gz" --repo Orkeon/orkeon
```

The second command prints the workflow that built it
(`.github/workflows/release.yml@refs/tags/v1.0.0-rc.3`) and fails on any byte
difference. Without `gh`, ask the public API directly — no token needed for a public
repository:

```bash
D=$(sha256sum "orkeon-cli-$VER-osx-arm64.tar.gz" | cut -d' ' -f1)
curl -fsSL "https://api.github.com/repos/Orkeon/orkeon/attestations/sha256:$D"
```

An empty `attestations` array (or a 404) means *no statement for these bytes*. Measured
on `orkeon-cli-1.0.0-rc.3-osx-arm64.tar.gz`: one statement, predicate
`https://slsa.dev/provenance/v1`, builder
`https://github.com/Orkeon/orkeon/.github/workflows/release.yml@refs/tags/v1.0.0-rc.3`,
eleven subjects (every archive, the `.deb` and both MSIs of that release).

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
update by itself; the packages it installs are the Release assets, so the attestation commands
above apply to them unchanged.

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
VER=1.0.0-rc.3
curl -fsSL -o "Orkeon.$VER.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/orkeon/$VER/orkeon.$VER.nupkg"

# 1. the nuget.org repository signature — intact since nuget.org stored it
dotnet nuget verify --all "Orkeon.$VER.nupkg"
#    Signature type: Repository — CN=NuGet.org Repository by Microsoft …

# 2. the bytes as attested — strip the appended repository signature
python3 scripts/nupkg-unsign.py "Orkeon.$VER.nupkg"
#    Orkeon.1.0.0-rc.3.unsigned.nupkg  sha256:5800062e39814ec846cae934d9344b85103b9134948cdff42b56a99c55cbb8e7

# 3. provenance of those bytes
gh attestation verify "Orkeon.$VER.unsigned.nupkg" --repo Orkeon/orkeon
```

Measured on `Orkeon.1.0.0-rc.3.nupkg`: the recovered digest has one statement, builder
`.github/workflows/publish.yml@refs/tags/v1.0.0-rc.3`, nine subjects — the six packages
pushed to nuget.org at that tag plus the three that only reached GitHub Packages
(`Orkeon.Compliance.Vfs`, `Orkeon.ConsoleApp`, `Orkeon.Generators`). Packages downloaded
from **GitHub Packages** are not repository-signed and verify directly, without the strip.
The exception is the dev channel (`<version>.dev.<n>`, see the
[publication matrix](../reference/publication-matrix.md)): those builds are not releases and
carry no attestation, so there is nothing to verify them against.

`scripts/nupkg-unsign.py` refuses to guess: if the signature is not the last entry of
the archive it stops rather than emit something that would fail verification anyway.

## Verify the software bill of materials

Every release cut since the SBOM step landed (2026-09-11 — so not `1.0.0-rc.3`, which
predates it) ships a CycloneDX SBOM next to its assets (`orkeon-<version>.sbom.cdx.json`) —
the full NuGet dependency graph of `Orkeon.sln`, transitive packages included, generated by
a pinned `CycloneDX` dotnet tool on the same runner right after the installers are
packaged, listed in `SHA256SUMS` and covered by the **same** attestation as the archives.
Verify it like any other asset:

```bash
VER=<version>   # a release that ships an SBOM
curl -fsSL -O "https://github.com/Orkeon/orkeon/releases/download/v$VER/orkeon-$VER.sbom.cdx.json"
gh attestation verify "orkeon-$VER.sbom.cdx.json" --repo Orkeon/orkeon
```

`publish.yml` produces the same SBOM for the package push, attests it with the packages,
and keeps it as a workflow-run artefact (`sbom`).

## What to do when a check fails

- `sha256sum` mismatch → the download is incomplete or altered; download again from the
  Releases page, never from a mirror.
- `gh attestation verify` fails on a **release asset** whose `SHA256SUMS` line matched →
  the manifest and the file agree with each other but not with any workflow run. Do not
  install; report it through the [security policy](../../SECURITY.md).
- `gh attestation verify` fails on a `.nupkg` **downloaded from nuget.org** → expected;
  strip the repository signature first (above). If it still fails after the strip, report it.
- The API returns a statement whose `workflow.repository` is not
  `https://github.com/Orkeon/orkeon` → the bytes were built elsewhere; report it.
