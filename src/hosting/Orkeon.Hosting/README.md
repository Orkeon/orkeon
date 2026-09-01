# Orkeon.Hosting

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Hosting** is the shared runner host: `RunnerHost.Build` wires the full Orkeon service stack (LLM providers, tool suites, VFS, tool registry) in the right order, and `RunnerExecution` drives batch or streaming crew runs — the same pattern the `orkeon` CLI and the example runners use, reusable from any web or console host.

## Install

This project is no longer distributed as a NuGet package — reference it from source (`ProjectReference` inside this repository); its assembly ships through the release artifacts. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Hosting & runner bootstrap](https://github.com/Orkeon/orkeon/blob/main/docs/reference/hosting.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
