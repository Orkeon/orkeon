# Orkeon.Compliance.Vfs

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Compliance.Vfs** is a Roslyn analyzer that forbids direct `System.IO` usage in framework code, routing all I/O through Orkeon's mount-aware, rights-audited virtual file system (`IFileSystemService`). 7 diagnostics (ORKVFS001-ORKVFS007), path-based exemptions, per-symbol opt-out attribute.

## Install

> **Not on nuget.org.** `Orkeon.Compliance.Vfs` is published only to the
> [GitHub Packages feed](https://github.com/orgs/Orkeon/packages) — the release workflow never
> pushes it to nuget.org, so `dotnet add package Orkeon.Compliance.Vfs` fails with `NU1101` against the
> default source. Add the feed first (GitHub Packages requires authentication even for public
> packages: a personal access token with the `read:packages` scope).

```bash
dotnet nuget add source https://nuget.pkg.github.com/Orkeon/index.json \
  --name orkeon-github \
  --username <your-github-username> \
  --password <PAT-with-read:packages> --store-password-in-clear-text

dotnet add package Orkeon.Compliance.Vfs --prerelease --source orkeon-github
```

See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md)
for the full list of what ships where.

Consumed at build time — reference it with `PrivateAssets="all"`.

## Documentation

- [VFS compliance](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/vfs-compliance.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
