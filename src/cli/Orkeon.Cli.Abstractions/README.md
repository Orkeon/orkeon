# Orkeon.Cli.Abstractions

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Cli.Abstractions** contains the pure contracts for interactive Orkeon CLI runners: `IInteractiveCommand`, the runner base, and the console adapter abstraction. Reference it to write commands without pulling any concrete console implementation.

## Install

This project is no longer distributed as a NuGet package — reference it from source (`ProjectReference` inside this repository); its assembly ships through the release artifacts. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [TypeScript CLI commands](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/cli-ts-commands.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
