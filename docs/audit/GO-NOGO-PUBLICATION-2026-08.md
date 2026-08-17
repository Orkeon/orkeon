# GO/NO-GO — Open-source publication audit (August 2026)

> **Date**: 2026-08-17
> **Scope**: the three repositories — `orkeon` (main, public-to-be), `backstage`
> (private), `experiments` (private) — **full git history included**.
> **Origin**: reconstitutes the June 2026 GO/NO-GO audits lost in the
> three-repository split; the three historical blockers are re-verified below.
> **Task**: backstage `tasks/PUB-01-audit-go-nogo-reconstitution.md`.
> **Language note**: `docs/audit/` holds dated snapshots and is excluded from
> the French-mirror parity contract (see CONTRIBUTING, "Bilingual
> documentation"); this report is English-only by design.

## Verdict

**GO**, with the remediations applied below. The two non-blocking
recommendations were resolved the same day (see the dedicated section:
Sonar key renamed to `Orkeon`, third-party reference submodule removed).

| # | Historical blocker | Verdict |
|---|---|---|
| 1 | SmartComponents / BGE-micro-v2 license | **Lifted** — MIT ↔ MIT, nothing redistributed by this repo |
| 2 | CrewAI trademark / fork attribution | **Lifted** — referential attribution kept, POSITIONING amended |
| 3 | Internal content in the repositories | **Lifted** — gitleaks green on full history ×3; residual personal traces remediated |

Next re-verification due: **before the first `dotnet nuget push` to a public
feed** (PUB-06), then at every major release.

## Blocker 1 — SmartComponents / BGE-micro-v2 license (lifted)

Facts verified on 2026-08-17:

- `Orkeon.Tools.Embeddings.Local` consumes **`SmartComponents.LocalEmbeddings
  0.1.0-preview10148`** as a plain NuGet dependency (`Directory.Packages.props:117`).
  **No code is copied and no model weights are redistributed** by this
  repository — the BGE-micro-v2 ONNX weights ship inside the upstream package.
- SmartComponents license: **MIT** — verified on
  [`dotnet/smartcomponents`](https://github.com/dotnet/smartcomponents)
  (the original repository was archived 2024-08-20 and moved under `dotnet`).
- Bundled model [`TaylorAI/bge-micro-v2`](https://huggingface.co/TaylorAI/bge-micro-v2):
  **MIT** — verified on the model card; distilled from `BAAI/bge-small-en-v1.5`
  (itself MIT).
- The root **`THIRD-PARTY-NOTICES.md`** already documented SmartComponents,
  bge-micro-v2 (verbatim license texts, retrieved 2026-06-11),
  Microsoft.Extensions.AI.Abstractions, AngleSharp, Apache.Arrow, and Jint —
  the June audit's licensing work survived the repository split. This audit
  re-verified the two model licenses against upstream (2026-08-17) and added
  the missing **section 7: ms-marco-MiniLM-L-6-v2** (the only weights actually
  redistributed by this repository, in `Orkeon.Rag.Onnx.Model` — Apache-2.0,
  SHA-256 hashes in the package-level notice file).
- No other binary model/native file is tracked by git (checked:
  `*.onnx|so|dll|dylib|gguf|bin|pt|safetensors`).

Residual risk: SmartComponents is an archived experimental project pinned at a
preview version — a supply-chain/staleness concern, not a licensing one
(tracked separately; candidate for a future vendoring decision).

## Blocker 2 — CrewAI trademark and fork attribution (lifted)

- **Decision**: the factual, one-sentence attribution stays in both READMEs
  (`README.md:272`, `README.fr.md:272`): *"Orkeon is an independent .NET
  framework for AI agent orchestration, inspired by CrewAI (MIT License)."*
  It is accurate, aligned with the project's honesty policy, and referential
  use of a mark to state inspiration is the legally safe posture. What remains
  forbidden: using the CrewAI mark in Orkeon naming/taglines/slogans,
  positioning as "CrewAI for .NET", or narrating a code-lineage story beyond
  that sentence.
- **POSITIONING.md amended accordingly** (backstage,
  `marketing/positioning/POSITIONING.md` §6 and §8 "Versus CrewAI") — the old
  blanket rule "never mention the fork origin publicly" contradicted the
  published README and is replaced by the rule above.
- Trademark registers (USPTO/EUIPO) were **not** queried from this
  environment; given CrewAI Inc.'s funding history a registered mark should be
  assumed. This does not change the analysis: the kept usage is referential
  only. Owner may optionally confirm via TESS/eSearch before launch.
- CHANGELOG history mentions the project's own renames
  (`CrewAI → Arkeon → Orkeon`, `CHANGELOG.md:676`) — **accepted**: it is the
  honest history of this codebase's naming, consistent with the policy.

## Blocker 3 — Internal content pass (lifted)

Method: `gitleaks 8.21.2` on **full history** (`--log-opts=--all`) of the three
repositories with their committed `.gitleaks.toml`, plus complementary greps
(emails, machine paths, private URLs) over the working trees, plus a review of
`llmproviders-test/` and the git author identities.

| Check | Result |
|---|---|
| gitleaks, main repo (269 commits) | **0 leaks** |
| gitleaks, backstage (67 commits) | **0 leaks** |
| gitleaks, experiments (66 commits) | **0 leaks** |
| Git author/committer identities (×3 repos, all history) | Single pseudonymous identity `Orkeon Contributors <arion@orkeon.org>` — clean |
| `llmproviders-test/` campaign reports | No captured `Authorization` headers or keys in tracked files; real `providers.json` is git-ignored |
| Committed LLM logging artifacts | None found |

Findings remediated by this audit (main repo; the literal values are
deliberately not reproduced here — this file is published):

1. **A personal email address** in the `CODE_OF_CONDUCT.md` /
   `CODE_OF_CONDUCT.fr.md` enforcement contact → replaced with
   `arion@orkeon.org` (matches the git identity).
2. **Personal `$HOME` mount paths** in
   `examples/06-engineering-devops/102-*/README.md` and `103-*/README.md`
   → neutralized to `/home/you/…`.
3. **A personal real-name GitHub URL** in the default HTTP User-Agent
   (`src/core/Orkeon.Domain/Constants/Http/HttpDefaults.cs`) → replaced with
   the organization URL `+https://github.com/Orkeon/orkeon`. This string is
   sent with every outbound HTTP request made by Orkeon tools — the
   highest-impact finding of this pass.
4. **A personal email address** in the Debian package maintainer field
   (`scripts/package-deb.sh`) → replaced with `arion@orkeon.org`.

Accepted without action (private repositories, illustrative or log content):

- backstage: first-name example paths in
  `features/filesystem-sandbox/SPEC-FILESYSTEM-SANDBOX.md`; one real
  workstation path (which also exposes the pre-rename project directory name)
  in `prompts/orkeon-csharp-fix-7-framework-frictions.md` (historical prompt).
- experiments: a workstation path in
  `01-codebase-analysis/rounds/round-12-results.md` (verbatim run log);
  synthetic French business emails in fixture datasets (`10-crm-nettoyage`,
  `11-tri-emails`) — fictional data.
- Main repo: a bare common first name used as the greeting-demo argument in
  the CLI scripting docs/examples/tests — no identifying context; kept.
- These remain acceptable **only while both repositories stay private**; if
  either is ever opened, re-run this pass first.

## Non-blocking recommendations — both resolved 2026-08-17

1. **SonarQube project key `CrewAI.NET`** (`scripts/sonar-analyze.{sh,ps1}`,
   `docs/guides/quality-gate.md` + FR mirror). A maintainer decision
   (QCM 2026-06-11) kept it for metric continuity on the self-hosted instance,
   but it was the last remaining public trace of the pre-rename project name.
   **Applied (same day, maintainer deferred the call back)**: default key
   renamed to `Orkeon` in both scripts, quality-gate guide (EN+FR) updated
   with the superseding decision and its accepted trade-off (analysis history
   restarts under the new key; `SONAR_PROJECT_KEY=CrewAI.NET` still browses
   the old project).
2. **`claude-code-system-prompts` submodule** pointed to a public third-party
   repository (Piebald-AI) used as development reference. Not a leak and not a
   redistribution (pointer only), but unrelated to the framework and
   surprising for public-repo visitors. **Applied (same day)**: submodule
   removed from the main repository; the reference (URL + last pinned commit)
   is archived in backstage (`prompts/REFERENCE-claude-code-system-prompts.md`).

## Coordinated decisions

- **Root cause of the "lost" June audits identified**: `/docs/audit/` was
  git-ignored by the OSS-migration section of `.gitignore` ("never
  published"), so the reports only ever existed on a local disk and did not
  survive the three-repository split. That rule is now lifted — audit
  snapshots are tracked, and are written to be publishable (no personal data
  reproduced).
- **`docs/audit/` is excluded from the FR parity perimeter** (dated snapshots,
  not living docs). Applied: `scripts/check-docs-parity.sh` now skips
  `docs/audit/`, and CONTRIBUTING(.fr) documents the exception. This also
  retires the four phantom FR mirrors that DOC-01 C2 listed for the lost
  audit reports.
