# Orkeon.Tools.Embeddings.Local.Package — packaging wrapper

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

This directory holds **no product code**. It is the PUB-25 packaging wrapper that packs the
`src/tools/Orkeon.Tools.Embeddings.Local` assembly as the public
`Orkeon.Tools.Embeddings.Local` NuGet package, with a nuspec dependency on the `Orkeon`
umbrella instead of the umbrella's constituent projects. The wrapped project keeps ordinary
`ProjectReference`s; only this wrapper carries the `PrivateAssets="all"` + embed pattern,
and the `PackageId` is applied at pack time because two projects cannot share a restore
identity in one solution.

**This file is not the packed README.** The nupkg's front page on nuget.org is
[`src/tools/Orkeon.Tools.Embeddings.Local/README.md`](https://github.com/Orkeon/orkeon/blob/main/src/tools/Orkeon.Tools.Embeddings.Local/README.md),
which this project packs at the package root.

This package stays **outside** the `Orkeon` umbrella on purpose: it carries a pre-release
SmartComponents dependency from an archived upstream, and folding it in would force that
pre-release on every consumer of the framework.

## Documentation

- [Publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md)
- [Opt-in subsystems](https://github.com/Orkeon/orkeon/blob/main/docs/reference/opt-in-subsystems.md)

MIT © Orkeon Contributors
