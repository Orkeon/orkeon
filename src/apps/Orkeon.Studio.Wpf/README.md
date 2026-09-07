# Orkeon.Studio.Wpf

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Studio.Wpf** is the Orkeon Studio desktop application: the team-creation wizard
(Describe ▸ Compose ▸ Try ▸ Adopt over `orkeon forge --events jsonl`), the teams screen,
the run screen and the unified settings screen. It targets `net10.0-windows` and its
assembly is named `Orkeon.Studio`; the launcher shipped in the archives is
`orkeon-studio` (`win-x64` only — WPF cannot cross-target). Light/dark themes and a hot
five-language switch (en, fr, es, de, zh-Hans) come from `Resources/Strings.resx` and its
four satellites.

## Distribution

`IsPackable=false`: on no NuGet feed. It ships inside `orkeon-cli-<version>-win-x64.zip`,
the full multi-app archive and the per-user MSI (with an "Orkeon Studio" Start-menu
shortcut) — see the
[publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Studio architecture](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/studio.md)
- [Three ways to run Orkeon](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/three-ways-to-run-orkeon.md)

MIT © Orkeon Contributors
