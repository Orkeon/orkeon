# Orkeon.Compliance.Vfs

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Compliance.Vfs** is a Roslyn analyzer that forbids direct `System.IO` usage in framework code, routing all I/O through Orkeon's mount-aware, rights-audited virtual file system (`IFileSystemService`). 5 diagnostics, path-based exemptions, per-symbol opt-out attribute.

## Install

```
dotnet add package Orkeon.Compliance.Vfs --prerelease
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

Consumed at build time — reference it with `PrivateAssets="all"`.

## Documentation

- [VFS compliance](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/vfs-compliance.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
