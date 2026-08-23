> 🇫🇷 [Version française](../fr/adr/ADR-005-famille-tools-heterogene.md)

> **See also**: [ADR-003 — Secondary shared kernels](./ADR-003-shared-kernels-secondaires.md) · [Back to the index](../INDEX.md)

# ADR-005 — Heterogeneous `Tools.*` family: `Tools.Web` and `Tools.EventHub` depend on `Application`

**Status**: Accepted · **Date**: 2026-06 · **Scope**: `Orkeon.Tools.Web` → `Orkeon.Application`; `Orkeon.Tools.EventHub` → `Orkeon.Application`

## Context

The `Tools.*` family of tool packages is not homogeneous in its dependencies:

- **Four packages** (`Tools.Code`, `Tools.Data`, `Tools.FileSystem`, `Tools.Analysis`) make do with
  `Orkeon.Tools.Abstractions` (+ abstractions), in line with the expected profile: a tool should
  depend only on the tool contracts and on `Domain`.
- **Two packages** (`Tools.Web` and `Tools.EventHub`) reach up to `Orkeon.Application`.

This asymmetry may surprise a contributor who expects all `Tools.*` packages to share the same
dependency baseline.

## Decision

**Accept and document** that `Tools.Web` and `Tools.EventHub` depend on `Application`, because their
tools need application services (ports/use cases) that `Tools.Abstractions` alone does not expose —
for example task coordination, A2A channels, or application context services.

The dependency direction remains correct: `Tools.Web`/`Tools.EventHub` are peripheral packages that
depend on `Application` (an inner layer), and not the other way around. No cycle, no inversion.

## Rejected alternatives

1. **Force every `Tools.*` package to depend only on `Tools.Abstractions`.** Rejected: this would
   require duplicating application contracts, or hoisting into `Tools.Abstractions` contracts that
   have no place in a package of pure tool abstractions, or introducing artificial indirections.
2. **Move `Tools.Web`/`Tools.EventHub` out of the `Tools.*` family.** Rejected: they are genuinely
   tool providers; their place in the `Tools.*` nomenclature is consistent for discovery and
   packaging.

## Consequences

- **Positive**: the implicit rule "a `Tools.*` package may depend on `Application` when its tools
  require application services" is now explicit. Reviews no longer have to settle this case on a
  one-off basis.
- **Vigilance**: prefer depending on `Tools.Abstractions` alone as long as a tool has no proven
  application-level need; only reach up to `Application` out of documented necessity.

## Amendment (2026-08-23)

The Context's package split has drifted — exactly the drift the Vigilance clause
exists to record. Current state: only `Tools.Code` still matches the
"abstractions-only" profile among the four originally listed. `Tools.Data`,
`Tools.FileSystem` and `Tools.Rag` now reference the concrete `Orkeon.Rag` (for the
RAG-backed search tools), which itself references `Orkeon.Application` (ADR-006
amendment of 2026-07-25) — so they reach `Application` transitively; `Tools.Analysis`
references the concrete `Orkeon.Analysis`. The decision itself is unchanged: reaching
up is legitimate when documented, and ADR-006 documents the RAG couplings.
