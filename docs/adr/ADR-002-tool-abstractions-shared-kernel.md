> 🇫🇷 [Version française](../fr/adr/ADR-002-tool-abstractions-shared-kernel.md)

> **See also**: [ADR secondary shared kernels](./ADR-003-shared-kernels-secondaires.md) · [ADR naming twins](./ADR-004-jumeaux-de-nommage-scripting.md) · [Back to the index](../INDEX.md)

# ADR-002 — `Orkeon.Tools.Abstractions` as the Infrastructure's *shared kernel*

**Status**: Accepted · **Date**: 2026-06 · **Scope**: `Orkeon.Infrastructure` → `Orkeon.Tools.Abstractions`

## Context

Orkeon's onion architecture enforces a strict dependency direction: `Domain` (core), then `Application`, then `Infrastructure`. `CLAUDE.md` summarizes this rule as "Infrastructure → Domain + Application".

Yet `Orkeon.Infrastructure` references a fourth project: `Orkeon.Tools.Abstractions`
(`src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj`, ItemGroup annotated `ADR-002`).
The usage is *load-bearing*, not marginal: **13 files** in the Infrastructure consume
`using Orkeon.Tools.Abstractions` (verified at commit `b5179b3c`). The Infrastructure indeed hosts
the concrete tool implementations (LLM tools, file system, orchestration), which need the base tool
contracts (`IBaseTool`, `ToolResult`, typed bases) to register and execute.

The question: is this reference a violation of the onion's dependency direction?

## Decision

Treat `Orkeon.Tools.Abstractions` as a **shared kernel**: a second abstractions core, on the same
footing as `Orkeon.Domain`, which the outer layers may depend on without breaking the onion rule.

Rationale: `Orkeon.Tools.Abstractions` **depends only on `Orkeon.Domain`**. It therefore introduces
no inbound dependency on `Application` or `Infrastructure`, and creates no cycle. It is a package of
pure interfaces and base classes — exactly the profile of an abstractions hub expected in an onion
architecture (high in-degree, minimal out-degree).

The `Infrastructure → Tools.Abstractions` reference is therefore a **deliberate architectural
exception**, documented by this ADR, and flagged at the source by an inline comment pointing to this
`docs/adr/` folder.

## Rejected alternatives

1. **Move the tool contracts into `Orkeon.Domain`.** Rejected: `Tools.Abstractions` aggregates
   contracts specific to the tool ecosystem (validation, batch, telemetry) that have no place in the
   pure business core; this would bloat the Domain and expose the tool contracts to every project,
   including those that have no need for them.
2. **Reimplement private tool contracts inside the Infrastructure.** Rejected: interface duplication,
   inevitable divergence from the `Tools.*` packages, loss of interoperability.
3. **Invert via a port interface in `Application`.** Rejected: the shared surface
   (`IBaseTool` and typed bases) is too broad to be reduced to a port; the shared kernel is more
   honest and simpler.

## Consequences

- **Positive**: a single source of truth for tool contracts, reused by the Infrastructure and the six
  `Tools.*` packages; no cycle; the onion rule is respected in spirit (a dependency on a stateless
  abstractions core).
- **Negative / vigilance**: `Tools.Abstractions` must **keep `Domain` as its only dependency**.
  Introducing any dependency on `Application`/`Infrastructure` in this package would turn the
  exception into an actual violation (cycle). To be watched in reviews.
- The inline comment in the `.csproj` must point to a real path (`docs/adr/`) — fixed in the same
  work item as this ADR.
