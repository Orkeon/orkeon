# Orkeon.Scripting.Cli

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Scripting.Cli** is the `orkeon` command-line tool: run YAML crews (`orkeon run crew.yaml`) and TypeScript scripts (`orkeon run script.ork.ts`), scaffold with `orkeon init`, diagnose with `orkeon doctor`.

## Install

```
dotnet tool install --global Orkeon.Scripting.Cli --prerelease
orkeon doctor
```

> Published to NuGet.org as part of the consolidated lineup (from `v1.0.0-rc.3`; earlier versions reached the GitHub Packages feed only) — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

Self-contained installers (Windows zip/MSI, Debian package, macOS tarball) are also published on the [releases page](https://github.com/Orkeon/orkeon/releases) — no .NET SDK required there.

## Documentation

- [Three ways to run Orkeon](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/three-ways-to-run-orkeon.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
