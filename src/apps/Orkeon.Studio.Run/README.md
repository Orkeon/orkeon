# Orkeon.Studio.Run

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Studio.Run** is the Studio run terminal app: pick a crew, launch it through the
shared process runner, and watch the run's events stream back. It builds on
`Orkeon.Studio.Core` and renders with Terminal.Gui.

## Distribution

`IsPackable=false`: on no NuGet feed. It ships as the `orkeon-studio-run` launcher in the
multi-app release archives and, on Debian/Ubuntu, at `/usr/bin/orkeon-studio-run` in
`orkeon_<version>_amd64.deb` — see the
[publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Studio architecture](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/studio.md)
- [CLI reference](https://github.com/Orkeon/orkeon/blob/main/docs/reference/cli.md)

MIT © Orkeon Contributors
