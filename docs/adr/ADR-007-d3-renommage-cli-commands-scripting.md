> 🇫🇷 [Version française](../fr/adr/ADR-007-d3-renommage-cli-commands-scripting.md)

> **See also**: [ADR-004 — Naming twins (superseded)](./ADR-004-jumeaux-de-nommage-scripting.md) · [Back to the index](../INDEX.md)

# ADR-007 — D3 settled: rename `Orkeon.Cli.Scripting` to `Orkeon.Cli.Commands.Scripting`

**Status**: Accepted · **Date**: 2026-08-17 · **Supersedes**: ADR-004 · **Closes**: decision D3 (publication gate), ORG-007
· **Scope**: `src/cli/Orkeon.Cli.Commands.Scripting`, `tests/cli/Orkeon.Cli.Commands.Scripting.Tests`

## Context

ADR-004 documented the near-anagram pair `Orkeon.Cli.Scripting` (library of
TypeScript-scripted interactive commands) / `Orkeon.Scripting.Cli` (the installable `orkeon`
dotnet tool) without settling the rename — it deferred it to a separate decision (ORG-007).
The NuGet publication matrix gated every ecosystem publish on that decision (**D3**), because
the rename window closes permanently at the first `dotnet nuget push`: PackageIds are locked
for life.

Three options were weighed (PUB-02 fiche): **A** keep both names and live with the permanent
cognitive collision; **B** rename the library (4,746 LOC, internal consumers only); **C**
rename the tool (483 LOC, but its PackageId *is* the `dotnet tool install` command — the most
publicly visible name of the project).

## Decision

**Option B.** The command library is renamed:

| | Before | After |
|---|---|---|
| PackageId / assembly / root namespace | `Orkeon.Cli.Scripting` | `Orkeon.Cli.Commands.Scripting` |
| Project folder | `src/cli/Orkeon.Cli.Scripting/` | `src/cli/Orkeon.Cli.Commands.Scripting/` |
| Test project | `Orkeon.Cli.Scripting.Tests` | `Orkeon.Cli.Commands.Scripting.Tests` |

`Orkeon.Scripting.Cli` (the `orkeon` tool) **keeps its name**: its PackageId is the install
command users type, and the ADR-004 mnemonic ("last segment `Cli` = the executable") stays
true everywhere it was anchored.

The rename cost is paid once, before anything is published: no NuGet package ever carried the
old name, so there is no redirect, no deprecation shim, no consumer breakage outside this
repository.

## Consequences

- **D3 is lifted** in `docs/reference/publication-matrix.md` — NuGet expansion now only awaits
  the maintainer's product confirmation (and PUB-03 wiring).
- The new name states the role directly: *commands* for the CLI, implemented via *scripting* —
  no longer a permutation of the tool's name.
- Historical mentions (`CHANGELOG.md`, ADR-004) keep the old name on purpose; everything
  active (code, solution, docs and FR mirrors, examples) uses the new one.
- ORG-007 is closed by this ADR.
