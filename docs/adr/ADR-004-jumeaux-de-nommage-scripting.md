> 🇫🇷 [Version française](../fr/adr/ADR-004-jumeaux-de-nommage-scripting.md)

> **See also**: [ADR-002 — Tool abstractions shared kernel](./ADR-002-tool-abstractions-shared-kernel.md) · [Back to the index](../INDEX.md)

# ADR-004 — Naming twins: `Orkeon.Cli.Scripting` vs `Orkeon.Scripting.Cli`

**Status**: Accepted (documents the existing state) · **Date**: 2026-06
· **Scope**: `src/cli/Orkeon.Cli.Scripting`, `src/scripting/Orkeon.Scripting.Cli`

## Context

Two projects carry near-anagram names, distinguished by a mere permutation of segments —
a permanent trap for issues, documentation, and NuGet searches:

| PackageId | Folder | Role | LOC (commit `b5179b3c`) |
|---|---|---|---:|
| `Orkeon.Cli.Scripting` | `src/cli/` | TypeScript-scripted **interactive commands** ↔ CLI. Adapter between `Orkeon.Cli.Abstractions` and `Orkeon.Scripting` (Jint + esbuild). | 4,746 |
| `Orkeon.Scripting.Cli` | `src/scripting/` | **Command-line entry point** of the scripting DSL: the installable tool `orkeon run script.ork.ts` (`PackAsTool=true`, `ToolCommandName=orkeon`, `AssemblyName=orkeon`). | 483 |

Both names are structurally valid (path = namespace suffix); this is not a convention drift, but a
**cognitive collision**: `Cli.Scripting` = "scripting *inside* the CLI",
`Scripting.Cli` = "the *CLI* of the scripting DSL".

## Decision

For the current version, **keep both names as they are and explicitly document their distinction**,
rather than renaming right away.

- The warning and the role of each project are recorded here and in `CLAUDE.md` (sections
  "Working Directory Structure" / "Important Notes"), so that any contributor or AI agent can
  disambiguate the two packages without having to inspect the `.csproj` files.
- The **actual renaming** (ORG-007) is a separate, heavier code work item (PackageId breakage,
  redirects, reference updates): it belongs to a separate human decision and is **not** settled by
  this ADR. This ADR documents the situation and provides the landmark; it does not commit to a
  target name.

## Consequences

- **Positive**: the most frequent confusion ("which one is the `orkeon` tool?") now has a single
  written answer. It is `Orkeon.Scripting.Cli` (final segment `.Cli` = the executable).
- **Vigilance**: until the renaming is done, any new mention in the docs or in issues must lift the
  ambiguity by restating the role (`orkeon` tool vs interactive CLI commands).
- **Mnemonic**: the project whose **last** segment is `Cli` (`Orkeon.Scripting.Cli`) is the
  **executable**; the one whose last segment is `Scripting` (`Orkeon.Cli.Scripting`) is the
  **command library**.
