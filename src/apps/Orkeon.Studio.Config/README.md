# Orkeon.Studio.Config

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Studio.Config** is the Studio configuration terminal app: the `orkeon init` flows
— model profiles, authorized folders, and the raw settings file — for platforms the WPF
desktop app does not reach. It builds on `Orkeon.Studio.Core` and renders with Terminal.Gui.

## Distribution

`IsPackable=false`: on no NuGet feed. It ships as the `orkeon-studio-config` launcher in
the multi-app release archives and, on Debian/Ubuntu, at `/usr/bin/orkeon-studio-config`
in `orkeon_<version>_amd64.deb` — see the
[publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Studio architecture](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/studio.md)
- [Configuration reference](https://github.com/Orkeon/orkeon/blob/main/docs/reference/configuration.md)

MIT © Orkeon Contributors
