# Orkeon.Rag.Onnx.Package — packaging wrapper

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

This directory holds **no product code**. It is the PUB-25 packaging wrapper that packs the
`src/rag/Orkeon.Rag.Onnx` assembly as the public `Orkeon.Rag.Onnx` NuGet package, with a
nuspec dependency on the `Orkeon` umbrella instead of the umbrella's constituent projects
(they already ship inside it). The wrapped project keeps ordinary `ProjectReference`s, so
in-repo consumers are untouched; only this wrapper carries the `PrivateAssets="all"` +
embed pattern, and the `PackageId` is applied at pack time because two projects cannot
share a restore identity in one solution.

**This file is not the packed README.** The nupkg's front page on nuget.org is
[`src/rag/Orkeon.Rag.Onnx/README.md`](https://github.com/Orkeon/orkeon/blob/main/src/rag/Orkeon.Rag.Onnx/README.md),
which this project packs at the package root.

The externals declared here are the wrapped assembly's dependencies that `Orkeon` does not
already carry; `scripts/check-package-closure.py` fails the build when that union drifts
from the real `PackageReference` set.

## Documentation

- [Publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md)
- [RAG pipeline](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/rag-pipeline.md)

MIT © Orkeon Contributors
