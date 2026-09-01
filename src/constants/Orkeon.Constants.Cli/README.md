# Orkeon.Constants.Cli

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET.

**Orkeon.Constants.Cli** declares the option names Orkeon's runners accept — `--settings`,
`--mount`, `--llm-log`, … — as bare names, without their leading dashes. It **depends on nothing**
([ADR-009](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-009-shared-constants-satellites.md)).

It exists because the same grammar was declared three times: the YAML runner's options, the
scripting runner's options, and Orkeon Studio's prediction of the command line it is about to
spawn. Studio composes an argument list for a process it does not reference; a flag renamed on one
side and not the other produces a launch that fails on an unrecognised option, at the one moment a
user has no console to read.

## Install

This project is no longer distributed as a NuGet package — reference it from source (`ProjectReference` inside this repository); its assembly ships through the release artifacts. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Contents

| Type | What it declares |
|---|---|
| `RunOptionNames` | The option names, bare (`settings`, not `--settings`), as `[Option(...)]` needs them — plus `Flag(name)` to compose the command-line spelling. |

## License

MIT
