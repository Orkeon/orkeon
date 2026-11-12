# Orkeon API reference

Generated reference for the public surface of the Orkeon assemblies listed in
`docfx.json` — the same surface that is frozen in each project's `PublicAPI.Shipped.txt`
(see [CONTRIBUTING — Versioning and API stability](../CONTRIBUTING.md#versioning-and-api-stability)).

This reference is published with the rest of the documentation at <https://orkeon.github.io/orkeon/>,
deployed by `docs.yml` on every `v*` tag.

**Documented is not the same as installable.** Distribution was consolidated in PUB-25: the
framework reaches NuGet.org as the `Orkeon` umbrella plus a short lineup of opt-in packages, and a
few assemblies ship only inside the `orkeon` CLI and the installer archives. Both groups are
documented here, because both are the API a caller writes against — but only the first group is
something you can `dotnet add package`. The authority on what ships where, package by package, is
the [publication matrix](../docs/reference/publication-matrix.md).

## Shipped in a NuGet package

Embedded in the `Orkeon` umbrella — `dotnet add package Orkeon` installs all of them at once:

- **`Orkeon.Domain`** — entities, value objects, domain events, core interfaces.
- **`Orkeon.Application`** — use cases, ports, DTOs, orchestration services.
- **`Orkeon.Infrastructure`** — LLM providers, memory stores, orchestration strategies, VFS.
- **`Orkeon.Constants.{Llm,FileSystem,Configuration,Protocol}`** — the shared-constants satellites
  (LLM endpoints and wire fields, virtual mount roots, configuration keys, the run-event vocabulary).
- **`Orkeon.Tools.Abstractions`** — the tool base classes and contracts.
- **`Orkeon.Analysis.Abstractions`, `Orkeon.Analysis`** — RaggableTree semantic codebase analysis.
- **`Orkeon.Rag.Abstractions`, `Orkeon.Rag`** — the RAG contracts and pipeline.
- **`Orkeon.Plugins`** — the plugin system
  ([building a plugin project](../docs/architecture/plugins.md#building-a-plugin-project)).

Opt-in packages of their own:

- **`Orkeon.Tools.{Analysis,Code,Data,Drive,Email,EventHub,FileSystem,Rag,Web}`** — the built-in
  agent tool families, packed together as `Orkeon.Tools`.
- **`Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model`** — the opt-in ONNX cross-encoder reranker and its
  embedded weights.
- **`Orkeon.Tools.Embeddings.Local`** — opt-in on-device embeddings.
- **`Orkeon.Scripting`** (with `Orkeon.Hosting`, the runner and host bootstrap — see the
  [distribution notes](../docs/reference/hosting.md#distribution)) — the TypeScript (`.ork.ts`) DSL
  runtime, run in-process.
- **`Orkeon.Constants.Cli`** — the run option grammar and exit codes shared between the CLI and
  external clients.
- **`Orkeon.Cli.Abstractions`**, **`Orkeon.Cli.TerminalGui`** — the interactive runner contracts and
  the Terminal.Gui console.
- **`Orkeon.Interop.AgentFramework`**, **`Orkeon.Hosting.Aspire`** — the Microsoft Agent Framework
  bridge and the .NET Aspire hosting integration.

## Documented for reference, distributed only inside the CLI and installer channels

These assemblies are on no NuGet feed, neither NuGet.org nor GitHub Packages, and are not part of
any lineup package. They ship as private implementation assemblies of the `orkeon` dotnet tool
(`Orkeon.Scripting.Cli`), the `orkeon-host` daemon and the `release.yml` installer archives. Their
API is documented for in-tree callers and for anyone building a host from a clone of the
repository — a `PackageReference` to either of them fails with `NU1101`.

- **`Orkeon.Cli`, `Orkeon.Cli.Commands.Scripting`** — interactive CLI runner building blocks.

`Orkeon.Scripting.Cli` (the `orkeon` dotnet tool) is also a NuGet package and has no pages here:
it is an executable entry point, not a library with a referenceable surface.

Types marked `[Experimental]` (diagnostic IDs `ORKEXP001`–`ORKEXP007`) are outside the
API stability commitment — see
[Experimental APIs](../docs/reference/experimental-apis.md).
