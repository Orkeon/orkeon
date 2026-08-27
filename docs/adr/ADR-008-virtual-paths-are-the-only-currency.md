> 🇫🇷 [Version française](../fr/adr/ADR-008-virtual-paths-are-the-only-currency.md)

> **See also**: [VFS compliance](../architecture/vfs-compliance.md) · [CLI reference](../reference/cli.md) · [Back to the index](../INDEX.md)

# ADR-008 — Virtual paths are the only currency agents are paid in

**Status**: Accepted · **Date**: 2026-08-25
· **Scope**: `Orkeon.Domain/FileSystem`, `Orkeon.Hosting`, `Orkeon.Host`, `Orkeon.Scripting.Cli`, `Orkeon.Studio.*`

## Context

Orkeon's filesystem doctrine was settled early and enforced hard: rule **Q3, "virtual paths
everywhere"** — no API, property or DTO carries a physical path outside `FileSystemService`
itself — backed by the `Orkeon.Compliance.Vfs` analyzer, by `MountInfo` being deliberately
`BasePath`-free, and by tests asserting that denial messages never name a disk path. When a CLI
flag once accepted a real path (`--prebuild-index <real-path>`), it was **removed** rather than
tolerated: the codebase is mounted with `-m ../path:/src:ro` instead.

The doctrine held inside the framework. It did not hold at the boundary where mounts are created.

Framework code computes absolute physical paths before any VFS exists — the crew definition's
own directory, the `--llm-log` directory. Those paths then have to be readable *through* the VFS.
The shortcut taken was to mount them **1:1**, physical equal to virtual:

```
{configDir}:{configDir}:ro          # RunnerExecution
{llmLogPath}:{llmLogPath}:rw        # RunnerExecution, RunCommand
{crewDir}:{crewDir}:ro              # HostCrewMounts, per hosted crew
```

so the absolute path already in hand resolved unchanged, "without any prefix gymnastics" as the
code put it. To make those strings parse, `FileSystemMount.IsValidVirtualPath` had been widened
to accept a Windows drive path as a **virtual** path. The breach was therefore in the VFS's own
contract, not in a caller.

The consequences were not theoretical. A mount parsed from a mount string gets
`MountVisibility.AgentFacing` — `Parse` has no way to say otherwise — so those directories were
listed to agents:

- `list_mounts` returned `C:\Users\…\my-team` as a mount **path**;
- every access-denied message names the available mounts, and
  `FileSystemService.CollectBasePaths` **deliberately exempted identity mounts from redaction**
  (redacting them would have produced `Available mounts: [REDACTED]`), so the guard was disarmed
  for exactly the mounts that needed it;
- `ShellCommandTool`'s path rewriting worked off the same list;
- where the mount table is rendered into a system prompt, an absolute disk path appeared under
  the sentence *"All file operations must use these virtual paths. Absolute or unmounted paths
  are not allowed."*

Studio then mirrored all of it faithfully — including into two screens a novice reads: the
Composer's folder chips and, worse, the agent editor's « Sur quel dossier » line, which joined
raw `physical:virtual:rights` strings under a label about what that agent can see.

There was never a technical need for the shortcut. The scripting path had always mounted its
input as `/script:ro`, the forge bench mounts `/workspace`, `/forge` and `/output`, and the crew
loader is path-agnostic: it reads whatever path it is handed through `IFileSystemService`.

## Decision

**A physical path is never a virtual path.** Concretely:

1. **Runners mount under a name.** The crew's directory is `/crew` (a single-file crew is
   addressed as `/crew/<file>.yaml`), a hosted daemon's crews are `/crews`, `/crews-1`, …, a
   script's directory stays `/script`. The loader receives the virtual spelling.
2. **`IsValidVirtualPath` is narrowed back to `/…` only.** A drive-letter virtual path is
   refused with a message that names the fix. The *physical* segment keeps its drive-letter
   handling — that side is legitimately a disk path.
3. **Infrastructure mounts are invisible.** `Orkeon:FileSystem:InternalMounts` carries mounts
   registered with `MountVisibility.Internal`: resolvable by the VFS, absent from
   `GetAvailableMounts()` and therefore from `list_mounts`, from prompt mount tables and from
   denial messages — a denial message is read by the LLM, so `FileSystemRegistry` builds its
   "Available mounts" and "Mounts granting …" lists from the agent-facing set, not from all
   mounts. The LLM exchange log lives there — the VFS must reach it, no agent has any
   business addressing it. It is a configuration key rather than a hosted service because the
   runners never start the host: an `IHostedService` would silently never fire under
   `--validate` or `--list-tools`.

   **`Internal` is a boundary, and it took a second accessor to make it one.** `Parse` had no
   way to say "hidden", so the first version of this decision only removed the mount from every
   *listing*: it stayed resolvable, and `IFileSystemService` had no notion of who was calling.
   The names are documented — `/llm-logs` holds every prompt and every API response of the run
   — so a single `file_read /llm-logs/llm-exchanges-….jsonl` handed an agent the whole exchange
   history. `FileSystemRegistry.ResolveAndCheckRights` now refuses an Internal mount by default,
   in both directions (`ToVirtualPath` will not name one either, so a tool's physical→virtual
   output rewrite cannot leak it), and reports it exactly like a path that does not exist. The
   two components that legitimately write to an internal root — the exchange logger and the
   code sandboxes — ask for `PrivilegedFileSystemAccess` by name: a distinct DI registration
   rather than a flag on the interface everyone already holds, so a tool cannot obtain it by
   accident and a reviewer can find every holder by searching for the type.
4. **The rule for screens is scoped, not absolute.** No physical path in an **agent-facing or
   novice** context. Expert surfaces — the effective-mounts table, the picker's mount-string
   preview — keep showing the real strings: their job is to state the exact command line.
5. **A user `--mount` claiming a reserved root is refused** with an actionable line, instead of
   surfacing as a duplicate-virtual-path exception thrown out of a DI factory.

The redaction carve-out survives for the one case left: a Unix mount spelled the same on both
sides (`/output:/output:rw`, the container convention). It no longer covers runner injection.

## Consequences

- An agent can no longer learn the operator's disk layout through the VFS, and denial messages
  are redacted unconditionally for every runner-injected directory.
- The exchange log stops being advertised to agents as a writable mount — it holds full prompts
  and API payloads.
- **Breaking**: `--mount` no longer accepts a drive-letter virtual path; the crew directory is
  `/crew` and the hosted crews `/crews*`. Nothing in the repository relied on the old spelling,
  and no published crew can: mount strings are supplied per launch, not stored in a crew.
- Turning `--llm-log` on no longer shifts `Orkeon:FileSystem:Mounts:{i}`, because the log rides
  its own key. Studio's index prediction gets simpler and stays true.
- Studio duplicates the three virtual roots (it cannot reference `Orkeon.Hosting`); a drift test
  pins them to `RunnerMounts`.

## What this ADR deliberately does not settle

A crew still **cannot declare the folders it needs**. `CrewYamlConfig` has no `filesystem:`
block, so the virtual→physical binding is supplied entirely from outside the portable artifact —
by a `--mount` argument, by `appsettings`, or by Studio's sidecar. That gap is why a promoted
team could be launched with no `/output` at all while its own tasks declared
`deliverable: /output/…`; the immediate breakage is closed by deriving those roots at promotion
and at adoption, which is a remedy at the edges, not the contract itself.

Declaring filesystem needs on the crew — on the model of the existing `links:` block, validated
by `orkeon run --validate` rather than failing at the first tool call — changes the YAML grammar,
the scripting DSL, the forge blueprint and public API in two assemblies. It belongs to a version
that is allowed to move the grammar, not to a release candidate. This ADR reserves the place.

The **privileged caller** this ADR left open has since been built: `PrivilegedFileSystemAccess`
is the second accessor, and `MountVisibility.Internal` refuses an agent that addresses the mount
by name instead of merely omitting it from the listings. What remains out of scope is finer
grain than one bit — per-caller rights, a capability handed to a specific tool — which would
touch the Domain contract far more deeply than one boolean on the registry does.
