# Orkeon API reference

Generated reference for the public surface of the thirty Orkeon assemblies listed in
`docfx.json` — the same surface that is frozen in each project's `PublicAPI.Shipped.txt`
(see [CONTRIBUTING — Versioning and API stability](../CONTRIBUTING.md#versioning-and-api-stability)).

**Documented is not the same as installable.** Distribution was consolidated in PUB-25: six
package IDs go to NuGet.org, and the rest of the framework reaches you inside the `orkeon` CLI
and the installer archives. Both groups are documented here, because both are the API a caller
writes against — but only the first group is something you can `dotnet add package`. The
authority on what ships where is the
[publication matrix](../docs/reference/publication-matrix.md).

## Shipped in a NuGet package

- **`Orkeon.Domain`** — entities, value objects, domain events, core interfaces.
- **`Orkeon.Application`** — use cases, ports, DTOs, orchestration services.
- **`Orkeon.Infrastructure`** — LLM providers, memory stores, orchestration strategies, VFS.
- **`Orkeon.Constants.{Llm,FileSystem,Configuration}`** — the shared-constants satellites carried
  by the umbrella (LLM endpoints and wire fields, virtual mount roots, configuration keys).
- **`Orkeon.Tools.Abstractions`** — the tool base classes and contracts.
- **`Orkeon.Analysis.*`** — RaggableTree semantic codebase analysis.
- **`Orkeon.Rag.Abstractions`, `Orkeon.Rag`** — the RAG contracts and pipeline.

The eleven assemblies above are the closure embedded in the single `Orkeon` package —
`dotnet add package Orkeon` installs all of them at once. The remaining shipped assemblies are
opt-in packages of their own:

- **`Orkeon.Tools.{Analysis,Code,Data,EventHub,FileSystem,Rag,Web}`** — the seven agent tool
  suites, packed together as `Orkeon.Tools`.
- **`Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model`** — the opt-in ONNX cross-encoder reranker and its
  embedded weights.
- **`Orkeon.Tools.Embeddings.Local`** — opt-in on-device embeddings.

## Documented for reference, distributed only inside the CLI and installer channels

These assemblies are `IsPackable=false`: they are on no NuGet feed, neither NuGet.org nor GitHub
Packages, and they are not embedded in the `Orkeon` umbrella. They ship as private implementation
assemblies of the `orkeon` dotnet tool (`Orkeon.Scripting.Cli`), the `orkeon-host` daemon and the
`release.yml` installer archives. Their API is documented for in-tree callers and for anyone
building a host from a clone of the repository — a `PackageReference` to any of them fails with
`NU1101`.

- **`Orkeon.Scripting`** — the TypeScript (`.ork.ts`) DSL runtime.
- **`Orkeon.Cli`, `Orkeon.Cli.Abstractions`, `Orkeon.Cli.Commands.Scripting`,
  `Orkeon.Cli.TerminalGui`** — interactive CLI runner building blocks.
- **`Orkeon.Hosting`** — runner & host bootstrap
  ([distribution notes](../docs/reference/hosting.md#distribution)).
- **`Orkeon.Plugins`** — the plugin system
  ([building a plugin project](../docs/architecture/plugins.md#building-a-plugin-project)).
- **`Orkeon.Constants.{Protocol,Cli}`** — the run-event vocabulary and the run option grammar
  shared between the CLI and Studio.

`Orkeon.Scripting.Cli` is the sixth NuGet package (the `orkeon` dotnet tool) and has no pages
here: it is an executable entry point, not a library with a referenceable surface.

Types marked `[Experimental]` (diagnostic IDs `ORKEXP001–004`) are outside the
API stability commitment — see
[Experimental APIs](../docs/reference/experimental-apis.md).
