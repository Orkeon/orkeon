> 🇫🇷 [Version française](../fr/adr/ADR-003-shared-kernels-secondaires.md)

> **See also**: [ADR-002 — Tool abstractions shared kernel](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR heterogeneous Tools.* family](./ADR-005-famille-tools-heterogene.md) · [Back to the index](../INDEX.md)

# ADR-003 — Secondary shared kernels: `Analysis.Abstractions` and `Analysis`

**Status**: Accepted · **Date**: 2026-06 · **Scope**: `Orkeon.Application` → `Orkeon.Analysis.Abstractions`; `Orkeon.Infrastructure` → `Orkeon.Analysis`

## Context

Two references cut across the "canonical" onion direction described in `CLAUDE.md`
("Application → Domain" / "Infrastructure → Domain + Application"), in favor of the RaggableTree
subsystem (`src/analysis/`):

1. **`Application → Orkeon.Analysis.Abstractions`**
   (`src/core/Orkeon.Application/Orkeon.Application.csproj`).
   **Minimal** usage: a single file consumes this project —
   `src/core/Orkeon.Application/Crew/DeliverableResolvers/FinalMessageResolver.cs`, for the
   `IInlineFqnValidator` interface, injected as an **optional** dependency.
   `Orkeon.Analysis.Abstractions` itself depends only on `Domain`; this is therefore not an inversion
   of the dependency direction, but the Application pays for an entire project reference (64 files of
   RaggableTree interfaces/DTOs) for a single interface.

2. **`Infrastructure → Orkeon.Analysis` (concrete, not just the abstractions)**
   (`src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj`).
   Usage **confined to DI composition**: `Orkeon.Analysis` is referenced only by
   `src/core/Orkeon.Infrastructure/DependencyInjection/RaggableTreeInfrastructureExtensions.cs`.
   The Infrastructure here plays a composition-root role to wire RaggableTree into the container.

## Decision

**Document and accept** these two couplings as they stand for the current version, without resolving
them immediately:

- `Orkeon.Analysis.Abstractions` is treated as a **secondary shared kernel** (same status as
  `Tools.Abstractions`, see [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md)): an abstractions
  project depending only on `Domain`, hence consumable by `Application` with no cycle and no
  inversion.
- The concrete `Infrastructure → Orkeon.Analysis` reference is accepted as **composition wiring**
  localized to a single DI extensions file, the Infrastructure assuming its composition-root role for
  the RaggableTree subsystem.

## Phase-out option (deferred)

This decision is not final. Phasing these couplings out remains possible and preferable in the long
run:

- **Application**: move the `IInlineFqnValidator` port into `Application` (or the interface into
  `Domain`), removing the reference to `Analysis.Abstractions` and making `Application` match
  `CLAUDE.md` ("Application → Domain").
- **Infrastructure**: lift the RaggableTree composition up into the host (the real application
  composition root, e.g. `ConsoleApp`), removing the concrete `Analysis` reference from the
  Infrastructure.

Until these phase-outs are carried out, this ADR serves as the consultable justification.

## Consequences

- **Positive**: the couplings are now **traceable and challengeable** instead of being blindly
  renegotiated at every review. Neither inverts the dependency direction nor introduces a cycle
  (verified at commit `b5179b3c`).
- **Vigilance**: `Analysis.Abstractions` must **keep depending on `Domain` alone**; any extension of
  the concrete `Analysis` usage in the Infrastructure (beyond the DI wiring) must reopen this ADR.
