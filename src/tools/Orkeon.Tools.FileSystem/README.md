# Orkeon.Tools.FileSystem

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.FileSystem** ships the file system agent tools (file read/write, directory listing and more), all routed through Orkeon's mount-aware, rights-audited virtual file system — agents only see the paths you mount.

## Install

```
dotnet add package Orkeon.Tools --prerelease
```

> This assembly ships inside the [`Orkeon.Tools`](https://www.nuget.org/packages/Orkeon.Tools) package — the seven built-in tool families in one install; it is no longer a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Tool inventory](https://github.com/Orkeon/orkeon/blob/main/docs/tools/inventory.md)
- [VFS compliance](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/vfs-compliance.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
