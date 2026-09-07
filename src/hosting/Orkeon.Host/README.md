# Orkeon.Host

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Host** is the service host daemon: a long-running process that holds a crew
registry, exposes a chat gateway, and drives channels (Discord today). Its assembly — and
the launcher in the archives — is named `orkeon-host`.

## Distribution

`IsPackable=false`, and **not a dotnet tool**: `dotnet tool install orkeon-host` resolves
nothing. It is an installer binary, shipped self-contained in the multi-app release
archives (with the `deploy/` tree: systemd unit, SCM registration script, Dockerfile.host)
and as the per-machine Windows service MSI `orkeon-host-<version>-win-x64.msi`, which
registers the `Orkeon` service under `NT SERVICE\\Orkeon`. See the
[publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Hosting reference](https://github.com/Orkeon/orkeon/blob/main/docs/reference/hosting.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
