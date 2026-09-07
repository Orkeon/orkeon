# Orkeon.Studio.Core

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Studio.Core** is the shared core of Orkeon Studio: the settings model, target
detection, the process runner that launches `orkeon` as a child, the Forge session model,
and the `IStudioStrings` localization port whose English defaults are the key registry of
record. It carries no UI — the three Studio fronts (WPF, and the two terminal apps) each
bind their own presentation onto it.

## Distribution

`IsPackable=false`: this assembly is on no NuGet feed. It reaches users inside the Studio
applications of the release archives and the Windows MSI — see the
[publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Studio architecture](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/studio.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
