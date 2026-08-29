> 🇫🇷 [Version française](../fr/adr/ADR-009-shared-constants-satellites.md)

> **See also**: [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR-006](./ADR-006-rag-subsystem.md) · [Back to the index](../INDEX.md)

# ADR-009 — A constant two projects must agree on lives in a satellite, not in a copy

**Status**: Accepted · **Date**: 2026-08-29
· **Scope**: `src/constants/*`, `Orkeon.Domain`, `Orkeon.Infrastructure`, `Orkeon.Hosting`, `Orkeon.Studio.Core`

## Context

Constants extraction is not the problem in this repository. It is largely done: **1030
`const string` across 270 files**, 51 of them in formal `Constants/` folders spread over seven
projects, in a shape that never varies — `static class` holding `const`, no records, no enums
pressed into service as string holders.

What is not solved is **agreement between projects that are forbidden from referencing each
other**. `Orkeon.Studio.Core` may not reference `Orkeon.Infrastructure` or `Orkeon.Hosting`:
Studio is a host application, and taking a dependency on the runtime's internals would invert
the layering the Clean Architecture rule exists to protect. But Studio needs the same values the
runtime uses — the LLM endpoint it writes into a settings file must be the endpoint the provider
will call, and the virtual root it predicts in a launch preview must be the root the runner
mounts.

The way out taken so far was to **copy the values by hand and guard the copies with a test**.
`tests/apps/Orkeon.Studio.Core.Tests/ConstantDriftTests.cs` exists for no other reason. It pins
eight families:

| Family | Source of truth | Hand-written copy |
|---|---|---|
| LLM endpoints (11) | `Constants/Llm/LlmEndpoints.cs` | `Studio.Core/Presets/OrkeonCliDefaults.cs` |
| Default model per provider (12) | `Constants/Llm/ProviderDefaults.cs` | `OrkeonCliDefaults.*DefaultModel` |
| Provider catalogue | `ProviderDefaults` | `Studio.Core/Presets/LlmPresets.cs` |
| Docker Model Runner (3) | `DockerModelRunnerDefaults` | `OrkeonCliDefaults` + `LlmPresets` + a committed template |
| Global settings path | `Hosting/RunnerSettings.cs` | `Studio.Core` `SettingsLocations` |
| Virtual mount roots | `Hosting/RunnerMounts.cs` | `Studio.Core` `MountAutoInjection` |
| Promoted crew folder | `ForgeRenderReader.CrewDirectoryName` | `RunTargetDetector.PromotedCrewDirectoryName` |
| WIN-01 wording | `RunnerHost.LlmNotConfiguredMessage` | `AppSettingsValidator.LlmNotConfiguredMessage` |

The copies say so themselves. `MountAutoInjection` documents the crew root as "Mirrors
`RunnerMounts.CrewVirtualRoot`; Studio.Core cannot reference Orkeon.Hosting, so a drift test pins
the pair."

A drift test is a real safety net, and it caught real drift. It is also the wrong shape of
solution, and it fails in a way that is easy to miss: it asserts **pairwise equality between the
constants someone remembered to list**. When `/sandbox` was added to the runner's reserved roots,
the mirror never gained it and the test could not notice — three equalities were still true.
The editor accepted `/sandbox` as a user mount while every runner refused it.

## Decision

**A value that two projects must agree on is declared once, in a satellite project that depends
on nothing, and both sides reference it.**

Satellites under `src/constants/`, one per family of vocabulary:

```
Orkeon.Constants.Llm             endpoints, default models, provider ids
Orkeon.Constants.FileSystem      virtual roots, conventional folder and file names
Orkeon.Constants.Configuration   Orkeon:* keys, settings paths, shared messages
Orkeon.Constants.Cli             run option names
Orkeon.Constants.Protocol        run event kinds
```

**As built, two corrections to the list above.** `Orkeon.Constants.Protocol` was not foreseen: the
run event stream turned out to be a wire protocol between two processes whose copies had already
drifted, and calling it configuration would have invited the next person to file a setting key
there. And `Orkeon.Constants.Llm` does **not** hold wire field names, though this ADR first said
it would — measurement showed the candidates were homographs (`"tools"` is a crew-YAML property in
Studio and an OpenAI request field in a provider; `"temperature"` is a sampling parameter in one
project and a body temperature in a test fixture), and the one family genuinely shared across the
boundary is already modelled by `LlmResponseFormat` in the Domain. Unifying unrelated strings
because they are spelled alike is worse than the duplication it removes.

Three rules bound them:

1. **Shared only.** A constant used by a single project stays in that project, in its existing
   `Constants/<Domain>/` folder. The satellites are not a warehouse; they are the place two
   projects meet. The 51 existing holders that nobody duplicates do not move.
2. **No runtime dependency.** A satellite references no project and no package. This is what
   makes it referenceable from anywhere, including `Orkeon.Domain`, without inverting anything.
3. **Constants and their own comparison helpers only.** No behaviour, no types that model a
   concept — those belong to Domain. `LlmRoles.Is()` is the upper bound of what a satellite may
   hold: a normalization helper over its own values.

### Why this does not break the layering

The layering rule forbids an inner circle depending on an **outer** one. A project with no
dependencies is not in a circle at all — it is a shared kernel, and this repository has already
accepted four of them on exactly that reasoning: ADR-002 (`Infrastructure → Tools.Abstractions`),
ADR-003 (`Application → Analysis.Abstractions`), ADR-005 (`Tools.Web/EventHub → Application`),
ADR-006 (`Orkeon.Rag.Abstractions`). What made each acceptable was that the referenced project
carries contracts, not infrastructure. A satellite holding `const string` carries even less.

**`Orkeon.Domain` may reference a satellite.** Domain has no project dependency today, and the
one `ProjectReference` it does carry — the source generator — is `OutputItemType="Analyzer"` with
`ReferenceOutputAssembly="false"`, so it vanishes at runtime. A satellite is different: it is a
genuine runtime reference, the first Domain will have. It is accepted because a project that
depends on nothing cannot drag anything in behind it, and because the alternative is what this
ADR exists to end — Domain keeping its own copy of a value someone else also holds.

### What "no dependencies" means precisely

`src/Directory.Build.props` injects the `Orkeon.Compliance.Vfs` analyzer as a `ProjectReference`
into every project under `src/`, satellites included. That reference is build-time
(`OutputItemType="Analyzer"`). So the promise is exact: **no runtime dependency**. A satellite
assembly ships alone.

## Consequences

- `ConstantDriftTests` shrinks by one assertion per family migrated, and is deleted when the last
  one goes. The proof of the change is a test disappearing, not a test being added.
- Four new packable projects: `.sln` entries, `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt`
  (mandatory — `OrkeonFreezePublicApi` defaults to true and makes RS0016/RS0017 errors), the
  project counts in `CLAUDE.md` that `scripts/check-doc-claims.py` verifies, and the publication
  matrix.
- Consumers gain a `using`. Nothing else about them changes: the values are identical, which is
  what the drift tests were asserting all along.
- A satellite is a published NuGet package. Its surface is frozen like any other, so a constant
  put there is a public commitment — which is the right bar for a value two projects agree on.

## What this ADR deliberately does not settle

- **Whether the existing 51 holders should eventually converge there.** They stay put. The rule
  is "shared goes to a satellite", and a holder nobody duplicates is not shared. If duplication
  appears later, the family moves then.
- **Tool names.** `public override string Name => "file_read"` is declared once, in the class
  whose identity it is. Moving it away buys nothing and costs a lookup. Only the *references* to
  a tool name from other projects are candidates.
- **User-facing text.** Shared wording (WIN-01) is a constant like any other, but text that
  belongs to Studio's localisation flows through `IStudioStrings` and stays there.
