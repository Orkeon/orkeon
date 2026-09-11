# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — the provenance chain is documented, verified on rc.3, and closed with an SBOM

The CI already did what almost no .NET open-source project does — Trusted Publishing by
OIDC, `actions/attest-build-provenance` on every package and every release asset,
`ContinuousIntegrationBuild` at pack time, `SHA256SUMS` per channel — and said so in one
line of one reference page. [Verify what you install](docs/guides/verify-what-you-install.md)
(EN + FR) now gives the exact gestures, each run against the published `v1.0.0-rc.3`
artefacts before being written down, and states what the chain does *not* prove.

Two measurements shaped the page. A release asset verifies as downloaded: the GitHub
attestation API returns one SLSA v1 statement for `orkeon-cli-1.0.0-rc.3-osx-arm64.tar.gz`,
builder `release.yml@refs/tags/v1.0.0-rc.3`, eleven subjects. A package downloaded from
nuget.org does **not**: nuget.org repository-signs every package by appending a
`.signature.p7s` entry, so its digest is no longer the attested one. The signature is
always the last entry, so `scripts/nupkg-unsign.py` (standard library, no re-zipping)
recovers the original bytes exactly — measured digest
`5800062e…cbb8e7` for `Orkeon.1.0.0-rc.3.nupkg`, which the API resolves to the
`publish.yml` statement with its nine subjects. `SECURITY.md` gains a *Verifying what you
install* section and the README installation table a *Verify what you download* row.

The one missing piece of the chain was the cheapest: both `publish.yml` and `release.yml`
now generate a **CycloneDX SBOM** of `Orkeon.sln` (`CycloneDX` dotnet tool 6.2.0, pinned;
199 components on rc.3) right after the build and cover it with the **same** attestation —
a release asset with its `SHA256SUMS` line, and a `sbom` run artefact for the package push.

### Fixed — `Orkeon.Compliance.Vfs` reaches NuGet.org, and works once it gets there

The analyzer was `IsPackable`, packed at every tag, and never pushed to nuget.org: the
publish lineup was a fixed list of six ids in two places. It is the seventh now, in all
seven hand-maintained copies `check-doc-claims.py` compares.

Pushing rc.3 would have shipped an inert package. Built against the repository's pinned
Roslyn 5.9.0, it was refused by the compiler of a stock .NET 10 SDK (10.0.301 ships
5.6.0) with `CS9057` — a *warning*, after which the analyzer is silently skipped. A fresh
project with a `File.ReadAllText` call built clean. The package is now compiled against
Roslyn 4.8.0 (the .NET 8.0.100 compiler; `VersionOverride` on the one reference), and the
same fresh project reports `ORKVFS001` and `ORKVFS002` as errors. Its README is rewritten
for a consumer who has never heard of `IFileSystemService`: the seven rules, the path
exemptions, the name-matched suppression attribute to declare locally, `.editorconfig`
severities.

### Changed — the front page stops asserting what `git tag` already says, and says who answers

The README and `CLAUDE.md` claimed "the latest tag is `v1.0.0-rc.2`" two days after
`v1.0.0-rc.3` was tagged and its six packages were on NuGet.org. Tag state is no longer
asserted in prose anywhere: `scripts/check-doc-claims.py` rejects the sentence shapes that
rotted (`not yet tagged`, `latest tag is`, and their French forms) and, when the clone
carries tags, fails the build if a `v*` tag newer than the props version exists — the
version bump that follows a release can no longer be forgotten silently. `ci.yml` checks
out with tags for that purpose.

`.github/CODEOWNERS` routed every review to `@Orkeon/maintainers`, a team that does not
exist — which GitHub treats as no owner at all. It now names the maintainer account.

`SUPPORT.md` gains an *If the project stops* section: MIT, a build reproducible from a
public clone, every action and base image pinned, no private infrastructure on the path —
so a fork that keeps the tests green is a full replacement. The README links to it.

`CONTRIBUTING.md` opens its *Areas for Contribution* with a scope freeze: no 15th LLM
provider, no new built-in tool, memory store, language adapter or orchestration mode
until real users ask — one maintainer carries the whole surface. The former wish list
(Cohere, Vertex AI, Qdrant, a web UI, calendar tools…) is gone; interoperability,
observability, tests, docs and bugs are what remains open. `limitations.md` records the
rule and the issue chooser links to it before a proposal is typed.

## [1.0.0-rc.3] - 2026-09-07

The release candidate that opens the repository. Since `1.0.0-rc.2`: the NuGet
distribution collapses from a per-layer lineup into a single `Orkeon` package plus
`Orkeon.Tools` and the opt-ins (PUB-25); the example catalogue drops its dedicated C#
trading runner and speaks TypeScript end to end; MiniMax and Grok bring the provider
fleet to **14** (Groq removed, no shims); the VFS boundary is enforced everywhere by its
own analyzer, and virtual paths became the only currency an agent is paid in (ADR-008);
constants shared by two projects moved to zero-dependency satellites (ADR-009); and a
full SonarQube campaign closed every issue above INFO — 0 bug, 0 vulnerability, 0
hotspot, technical debt down from 1 762 minutes to zero, A on all four ratings.

The public API surface is frozen at this tag: the 328 additions and 203 removals
accumulated since rc.2 move from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`
across the twelve projects that carried them, and the seven `ORKVFS` analyzer rules ship
with them.


### Changed — the scripting DSL stops dropping half of what a script declares in silence

A `.ork.ts` file picks one of two engines by how it ends, and each engine honours what the
other ignores. A crew that declares tasks and ends with `await crew.run()` ran its agents
and never looked at the tasks; a crew that declares `.body()` and hands itself off with
`globalThis.crew = crew` ran its tasks and never invoked a body. Both produced a run that
succeeded, printed a plausible result, and said nothing about the half it had thrown away.

Both halves now warn, symmetrically and by name. `JsCrew.RunAsync` reports the tasks it
will not read (with the count), a manager it will not use, and a `process(...)` that
reaches only a telemetry tag. `JsCrewConfigurationAdapter.CollectIgnoredFeatures` reports
`.body()`, `.withState`, `.onError`, `.onAgentStart`/`.onAgentStop`, `budget()` and the
crew hooks — each naming the agent it was written on, because "a body was dropped" in a
six-agent crew is a second search. The runner logs them; `orkeon run` also warns on stderr
when `--inputs`, `--inputs-file` or `--memory-limit-mb` is passed to a script that hands its
crew off, since those options have no equivalent on that path and `globalThis.inputs` is
never planted.

`.concurrency(n > 1)` is deliberately not in that list: `JsAgentBuilder.build()` already
throws on it, so no crew carrying one can reach the adapter. A warning for an impossible
state is noise.

### Removed — `crewBuilder().graph()`, a method whose argument no engine ever read

`.graph(stateGraph)` stored its argument in a private field that reached neither
`JsCrewDefinition` nor `JsCrew` nor the declarative adapter — and `process("graph")` refused
to build without it. The one crew mode that needed the method was gated behind a method that
discarded what it was given, and no file in the repository ever called it.

The two facilities share a word and nothing else. `process("graph")` runs the crew on the
domain's graph strategy — a fixed `agent_execute → route_decision` loop with a circuit
breaker, tuned by `GraphConfig`. `stateGraph({ nodes, edges })` is the topology the script
draws, and it runs on `.run()` from an agent `.body()`. Removing the method unblocks the
mode: `process("graph")` now builds on its own. Same treatment as `when()` earlier in this
release, and for the same reason — nothing is released at rc.3, and this repository takes no
compatibility shims.

### Fixed — the ONNX crash CI had been tolerating was a call into a disposed session

`LocalEmbeddingProvider`, shipped in the `Orkeon.Tools.Embeddings.Local` package, set a
`_disposed` flag in `Dispose()` and never read it again. `EmbedBatchAsync` and
`Dimensions` went on to use the `LocalEmbedder` whose native ONNX session had just been
freed. Both now throw `ObjectDisposedException`, so a disposed provider refuses the call
instead of dereferencing freed memory — which for a library means it can no longer take
its host process down at shutdown.

The visible symptom was a test suite that killed its own process.
`Dispose_Releases_Embedder` asserted that a disposed provider "raises any exception",
an assertion written over undefined behaviour. On a warm heap the freed session returned
nonsense (`OnnxRuntimeException: input name cannot be empty`); on a dirty one it was a
SIGSEGV. Measured: the class crashed 5 runs out of 5, while each of its tests run alone
crashed 0 out of 3 — the crash needed the earlier tests to dirty the heap first. After
the fix, 17 consecutive runs exit 0 with all 11 tests green. The test now asserts
`ObjectDisposedException` exactly, and a second one covers `Dimensions`.

That crash had been recorded as an ONNX Runtime teardown artefact (PUB-17 / SONAR-14).
`ci.yml` and `publish.yml` each carried a step tolerating exit 139 whenever "every
discovered test was accounted for" — but the accounting came from the crashed process
itself, so a truncated run always matched, and a crash landing before the first result
reported zero tests and failed the step anyway, which is how the CI run of 2026-09-07
went red. Both steps are plain `dotnet test` runs again, `integration.yml` no longer
excludes the project from the nightly sweep, and `docs/reference/limitations.md` (EN+FR)
now records what the crash was instead of what it was taken for.

### Fixed — the final pre-publication review: what eleven reviewers found in the tree they were about to make public

**Security.** Web tools no longer share the host's ambient `HttpClient`:
`AddOrkeonWebTools` registers its own named client
(`WebToolExtensions.HttpClientName`) with `AllowAutoRedirect = false`, closing an
SSRF-by-redirect hole where a validated first hop could redirect a request to a
metadata endpoint the guard never saw. The RAG `WebPageLoader` gained the guard
it never had: it validates through `IUrlValidator` before any fetch and **fails
closed** when no validator is registered, so `rag_ingest` can no longer be
pointed at `169.254.169.254`. Both SSRF guards learned the IPv6 addresses they
were letting through -- `::`, `ff00::/8` multicast, the `64:ff9b::/96` NAT64
prefix, and IPv4-compatible `::x.y.z.w` forms judged against the IPv4 table --
and a new test pins the two tables against each other so they cannot drift.
`http_api` now runs LLM-supplied headers through the header sanitizer instead of
forwarding them verbatim, with a default sanitizer backing every construction
shape, so a model can no longer set `Host` or `Cookie` or smuggle a CRLF. The
shell tool clears the child's environment down to a named allowlist rather than
handing it every variable the host process holds, and its read-only `git`
guarantee inspects every token instead of the subcommand alone (`git log
--output=/tmp/x` was a write). `LogSanitizer` learned the key shapes this
repository actually ships -- underscores inside keys, `xai-`, `hf_`, `tgp_v1_`,
`tvly-`, `xox[abp]-`, `AIza`, and the dotted DashScope form -- and the two
`sk-` patterns became one, which also fixed a case where the narrower pattern
matched first and left the tail of a key in the log.

**Contracts that lied.** `SandboxOptions.RequireHumanApproval` and
`PreferredSandbox` were public, documented, bound from configuration and read by
nothing; an option that promises a human gate and does nothing is worse than no
option, so both are **removed**. `LlmConfig.ApiKey` stopped being `[Obsolete]`:
it steered every caller to `ApiKeySecretName`, which the framework never
resolves, so following the compiler produced an unconfigured provider --
`ApiKeySecretName` is now documented as reserved and the limitation is recorded.
Azure OpenAI's direct streaming path threw instead of ending on silence, joining
what the other providers already did; and the three buffered streaming fallbacks
(`RateLimitedLlmProvider`, `HttpLlmProviderBase`'s default, the `IChatClient`
adapter) stopped swallowing a provider's refusal and re-emitting it as an empty
stream.

**Distribution.** Both tool packages redistribute ONNX model weights, and neither
carried `THIRD-PARTY-NOTICES.md`; the notices now travel with them, in the two
nupkgs, the installer archives and the Debian package. The package-closure gate
walks the whole transitive `ProjectReference` tree, so an assembly that ships in
no lineup package fails the gate instead of reaching a consumer as a
`FileNotFoundException`.

**Command line.** `orkeon --help`, `orkeon help` and a bare `orkeon` print a
usage page listing every verb and exit 0, instead of listing nothing and exiting
1; an unknown first token is rejected by name rather than falling through to
`run`.

**Studio.** Three English catalogue values still named French buttons; the
orphan-key drift gate filtered on a key prefix no key carries, so it checked
nothing; and a resumed forge session dropped most of the metrics
`last-run.json` carries.

**Documentation.** The pages stopped describing a repository that does not
exist: the install lines carry `--prerelease`, the phantom `--config` flag became
the positional `<config>` the CLI really takes, `examples/README.md` names the
environment variable the runtime actually reads, the release pipeline is drawn
from `release.yml`'s own job graph, the quality-gate policy dates its
measurements against the September report, `SECURITY.md` admits that
`shell_command` ships registered, the runner logging default is Warning and says
so, the English Studio page quotes the English catalogue, and the documentation
site has a published address. Every `src` project has a README.

**Two more guards, caught by the same pass.** The Guardian's own SSRF check was the
third guard on this surface and had kept a hand-written IPv4 regex while the other
two were hardened; it now judges through `UrlValidator`'s tables, so `[::1]`,
`[::]`, the IPv4-mapped and NAT64 forms of the metadata endpoint and `100.64.0.0/10`
are refused where they used to pass. And `DockerSandbox` — the boundary `SECURITY.md`
points at for untrusted code — started its container as root with the daemon's
default capability set; the run line now always carries `--cap-drop=ALL`,
`--security-opt=no-new-privileges` and `--pids-limit`.

**Three more, on the same security surface.** The URL validator logged the credentials
it had just refused: an embedded-credentials denial wrote the password into a Warning
line, and the scheme and port denials logged the query string. Log lines now carry
scheme, host and port only. The VFS analyzer could not see `using static System.IO.File;
ReadAllText(p)` nor `FileStream fs = new(path, …)`, so both shapes escaped ORKVFS001-006;
it now judges invocations on the resolved symbol and registers the target-typed creation
form. And the sandbox's Roslyn denylist ignored `Environment.Exit`,
`Environment.GetEnvironmentVariable` (the environment holds every API key the host
resolved), `AppDomain.CurrentDomain.Load(bytes)`, the target-typed `Process p = new();`
and `unsafe` used as a method modifier — all five are now flagged.

**Gates.** `check-doc-claims.py` compares the CONTRIBUTING copies of the NuGet
lineup (six copies now, not four) and checks the push order in each;
`check-comment-accents.py` reaches unaccented French in C# comments, which is how
twenty-seven comments quoting French UI labels had survived the catalogue
rewrite.

### Fixed — a provider that cannot stream now says so, and the clocks stop deciding tests

- **`ILlmProvider.SupportsStreaming` follows `IsConfigured`** instead of being an
  unconditional `true` on all 14 providers. An unconfigured provider used to declare
  streaming, the caller took the SSE branch, and the branch ended without a single chunk —
  a script's `for await` completed on a silence indistinguishable from a model with nothing
  to say, while the very same provider's buffered path said "API key is required" out loud.
  A direct streaming call on an unconfigured provider now **fails in the open**, and the
  streaming and buffered paths render the same marker. Consumers that branch on the
  declaration (`LlmProviderToChatClientAdapter`, `RateLimitedLlmProvider`, the scripting
  `llm` facade) see it change with the configuration.
- `AgentWorkloadTracker` and `TaskExecutionRouter` take an injected `TimeProvider`. Five
  test classes stop measuring wall-clock time — three of them were assertion bugs rather
  than timing ones.
- Publishing hardening: both release workflows attach **build provenance attestations**,
  the container base images are pinned **by digest** (Ollama stops floating on `:latest`),
  `NuGetAudit` is declared rather than inherited as a side effect of `-warnaserror`, and a
  tag carrying a prerelease suffix now publishes as a **prerelease** instead of becoming
  `Latest` by omission.
- The secret-scan allowlist stops masking whole files and **names the individual values** it
  accepts, so a real credential added to an allowlisted file is still caught.
- Two tests stopped depending on the machine: the `Retry-After` policy test reads a
  **monotonic** `Stopwatch` instead of `DateTime.UtcNow`, and the crew-host start/stop test
  waits for the hosted loop to announce itself instead of racing it.
- The strict docfx build (`--warningsAsErrors`) is green again — the API landing page is
  reachable from the table of contents, and the generated SonarQube reports joined the
  site's content set instead of dangling as broken links.
- The LLM campaign kit (`llmproviders-test/`) generates its reports and its regenerated
  index in **English**; reports dated before 2026-09-06 stay French, and the index says so.

### Fixed — SonarQube campaign: 186 issues resolved, BLOCKER through MINOR **[breaking — constructor shapes]**

A full analysis (SonarQube 9.9.8, scanner .NET 11.2.1, `Sonar way` C# profile) on
the renamed `Orkeon` project reported 186 issues in the BLOCKER..MINOR band across
119 files, plus 17 security hotspots left `TO_REVIEW`. All 186 are now closed and
every hotspot is reviewed: **0 bug, 0 vulnerability, 0 hotspot, 0 issue above INFO**,
technical debt 1 762 min to **0**, and reliability / security / maintainability /
security-review all rated **A**. Coverage (82.7 %) and duplication (1.6 %) are
unchanged — this campaign moved no test and added no dead code.

- **11 BLOCKER `S2699`** — tests that asserted nothing now assert what they claim to
  verify (`Orkeon.Host.Tests`, `Orkeon.Infrastructure.Tests` EventHub, `Callback`,
  `Analysis` hybrid search, `Studio.Core`).
- **42 `S3776`** — cognitive complexity brought under 15 by extraction, not by
  splitting: `ForgeCommandOptions.Parse` (58), `ForgePromote.WriteCard` (50),
  `ForgeEngine.RunAsync` (30), `RagNamespaceBinding` (26), `RunnerExecution` (21),
  `LlmProviderFactory` (19), `OpenAICompatibleProviderBase` (19), and 35 more.
- **35 `S3358`** nested ternaries, **18 `S107`** over-long parameter lists,
  **11 `S125`** commented-out code, **7 `S3267`** loops replaced by LINQ,
  **5 `S1168`** null collections, and the mechanical tail (`S927`, `S1186`, `S1144`,
  `S2365`, `S2479`, `S1854`, `S3264`, `S3871`, `S2223`, `S1751`, `S3903`, ...).
- **20 findings arbitrated as false positives**, each carrying an in-code
  `[SuppressMessage]` (or a local `#pragma`) that states why: `S101` on `I18n`
  (the rule proposes `18N`), `S3604` on `Lock _gate = new()` fields of primary-
  constructor types, `S107` on `[LoggerMessage]` partials, `S1168` where `null` is
  a third state the caller reads (`ICrewLinkProvider.LinksFor`, the shell-tool
  allowlist), `S2737` on catch clauses that exist to carry an exception filter,
  `S3925` on the EventHub exceptions, `S3871` on an executable-internal exception.
- **17 security hotspots reviewed SAFE**: 13 x `S2077` (the only interpolated
  fragment is an allowlist-validated, quoted SQL identifier — SQLite cannot
  parameterize identifiers in DDL/PRAGMA; every caller value is a command
  parameter), 2 x `S4792` (the framework's own logging wiring), 2 x `S5443`
  (`/tmp` is a virtual VFS path, not the shared OS temp directory).

**Breaking (source):** four constructors that took more than seven dependencies now
take a single grouped dependency object — a `sealed record` for the three RAG ones, a
`sealed class` for `CrewStrategyDependencies`. No shim is provided — call sites move
with them.

- `Orkeon.Infrastructure.Crew.Strategies.CrewStrategyDependencies` (new) replaces the
  `(taskRepository, agentRepository, executionService, memoryScope)` quadruple in
  `SequentialProcessStrategy`, `GraphProcessStrategy`, `AutonomousProcessStrategy`
  and `ConsensualProcessStrategy`.
- `Orkeon.Rag.Pipeline.StagedRagPipelineDependencies` and
  `Orkeon.Rag.Pipeline.IngestionPipelineDependencies` (new) replace the optional
  collaborator tails of `StagedRagPipeline`, `CorrectiveRagPipeline` and
  `DefaultIngestionPipeline`.
- The remaining `S107` sites in `Orkeon.Scripting` and `Orkeon.Hosting` follow the
  same shape; every move is declared in the affected projects' `PublicAPI.Shipped.txt`
  (the `Unshipped` files stay header-only, as the release-readiness gate requires).

Out of band and left as-is: 406 `INFO` issues, 404 of them `xUnit2033` (use the
value `Assert.Single` returns instead of re-indexing) plus two `SYSLIB` hints.

### Changed — the example catalogue speaks TypeScript, and the trading assembly retires **[breaking — one package removed]**

The numbered catalogue had two ways to run: the `orkeon` CLI for most of it, and a
dedicated C# runner for the fifteen finance crews, dragging a 14 000-line assembly and
its own package behind it. It now has one.

- **The fifteen finance crews are `main.ork.ts`** — every process type (hierarchical
  with managers, parallel fan-outs, sequential, consensual), memory, the three
  `humanInput` reviews, dependency DAGs, built-ins by name, and their tools attached as
  instances. All validated end to end through the same strict pipeline as YAML. The
  catalogue holds at 105 examples.
- **A shared `_tools` TypeScript module** replaces `Orkeon.Trading.Tools`: 27 tools over
  seven category files plus a small financial-math core (correlation and covariance
  matrices, Acklam's normal inverse CDF, a seeded mulberry32 PRNG). Every tool is
  **deterministic** — same input, same output — which the `Random`-based C# originals
  never were. Simplifications where the C# leaned on MathNet are documented in place
  (grid-scan mean-variance, convex-blend Black-Litterman, midpoint-bisection HRP);
  everything else is a faithful port, Wilder's full ADX included. The seventeen tools no
  finance config ever referenced were not ported.
- **Removed**: `Orkeon.Trading.Tools` (162 files), the trading runner, the two
  interactive example runners and the `examples/_shared` library — from both solutions
  and from the disk. `publish.yml` **no longer packs `Orkeon.Runners.Shared`**: that
  package is discontinued. `MathNet.Numerics` and `YahooFinanceApi` leave the package
  versions with no consumer left, the container image drops its trading publish stage and
  banner line, and the installers drop the `orkeon-trading` launcher.
- **A declarative `.ork.ts` now runs through the real pipeline.** A bare
  `orkeon run crew.ork.ts` used to execute a flat loop over agent bodies that ignored
  tasks, process, manager, `humanInput` and deliverables — the full orchestration was
  reachable only behind `--config`. `run` now routes to the shared one-shot runner
  whenever the script declares the `globalThis.crew` handoff, so the script is evaluated
  exactly once, by the pipeline. Procedural scripts keep the script path untouched.
- Every surface that discovers, validates, lints, indexes or launches the catalogue
  accepts `main.ork.ts` beside `config.yaml`: `run-example`, `test-all-examples`,
  `validate-all-examples`, the config linter (which grew a TypeScript lint checking every
  quoted tool name against the manifest and the shared module), the index generator, and
  the container's `orkeon-example` dispatch. The guides say one CLI, EN and FR.
- Incidental: a Cyrillic-homoglyph agent id the 36 finance YAML files had carried since
  birth is ASCII at last, and the catalogue-wide `CA5394` waiver lifts — the seeded-PRNG
  TypeScript successors made it moot.

### Changed — one `Orkeon` package instead of a per-layer NuGet lineup (PUB-25) **[breaking — packaging only]**

The Domain/Application/Infrastructure split is an internal discipline, not a
distribution contract — and shipping it as three packages had already produced
one real incident (rc.1/rc.2 published on NuGet.org with five unrestorable
`Orkeon.*` dependencies). Distribution is now consolidated; the 39-project
source layout, namespaces, and per-assembly PublicAPI freeze are untouched, so
**consumer code compiles as-is** — only the install line changes.

- **`Orkeon`** (new): the whole framework in one package — eleven embedded
  assemblies (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`,
  `Orkeon.Rag(.Abstractions)`, `Orkeon.Analysis(.Abstractions)`,
  `Orkeon.Tools.Abstractions`, and the three constants satellites of the core
  graph). Migration: uninstall the per-layer packages, `dotnet add package
  Orkeon --prerelease`.
- **`Orkeon.Tools`** (new): the seven built-in tool families in one package,
  separate from `Orkeon` only for dependency weight (database drivers, PDF and
  spreadsheet libraries live here). Depends on `Orkeon`.
- `Orkeon.Rag.Onnx` and `Orkeon.Tools.Embeddings.Local` now depend on the
  `Orkeon` package instead of the discontinued per-layer ones.
- The per-layer, per-family and deferred-library packages (`Orkeon.Domain`,
  `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.*`,
  `Orkeon.Tools.<family>`, `Orkeon.Cli.*`, `Orkeon.Scripting`,
  `Orkeon.Hosting`, `Orkeon.Plugins`) are no longer packed; the published
  rc.1/rc.2 of the three core packages are to be unlisted on NuGet.org.
- `publish.yml` pushes the new lineup (`Orkeon`, `Orkeon.Tools`, the ONNX
  reranker pair, the local-embeddings tool package, and the `orkeon` dotnet
  tool) and a new gate — `scripts/check-package-closure.py` — fails the
  workflow if a lineup package depends on an `Orkeon.*` id outside the lineup
  or if an umbrella's hand-declared external dependencies drift from its
  embedded projects.

### Fixed — publishing hygiene ahead of the public opening

- Release builds emit **embedded portable PDBs** again (`DebugSymbols=false`
  had silently disabled emission, shipping non-debuggable packages with no
  SourceLink attachment point); `EmbedUntrackedSources` is on.
- The `orkeon` dotnet tool package no longer bundles the iOS/Android
  onnxruntime natives a CLI tool can never load: 262.5 MB → 137.6 MB, back under
  the nuget.org size limit.
- The docfx API reference now covers the five `Orkeon.Constants.*` assemblies,
  and `namespaceLayout: flattened` removes the 26 dead breadcrumb links the
  nested layout generated.
- A missing `--mount` source directory now fails with a clean actionable
  `ERROR:` line and exit 1 — in the YAML runner, `--validate`, `--list-tools`
  and `orkeon run` alike — instead of a raw dependency-injection stack trace.
- `CrewConfigurationMapper` reports skipped tools and failed LLM-provider
  resolution through an optional `ILogger` (source-generated warnings) instead
  of writing to the console from the Application layer; the `ExampleCallbacks`
  demo handlers moved out of the published `Orkeon.Application` assembly into
  the test suite that was their only consumer.
- Supply-chain hardening: a root `nuget.config` pins nuget.org as the only
  package source (with wildcard source mapping); esbuild moves to `^0.25.12`
  (GHSA-67mh-4wv8-2f99) and every packaging-time esbuild tarball download now
  verifies the sha512 integrity recorded in the lockfile; Dependabot watches
  the Docker base images (root + deploy).

### Changed — the screenshot campaign photographs an application in use, not an empty one

`orkeon-studio --capture-screens <dir>` existed to be the fidelity reference against the v3
mock, and could not be: it built the window over `CreateForCurrentMachine`, so on a clean
machine every shot was an empty card — no team, no history, no session, a wizard frozen on
step 1. Twenty-two images over roughly a hundred visual states, one theme out of the two the
v3 remediation promised, and no test at all beyond argument parsing.

The campaign now builds the window over a **seeded scenario in a throwaway temp directory** and
walks every screen and every gated state of it, in both modes and both themes, plus a language
sweep over the densest screens.

- **Two worlds, never one mutated into the other.** A populated machine (three adopted teams —
  one of which names a folder nobody declared —, seven past runs, four forge sessions, four
  model profiles, a declared folder that genuinely does not exist on disk, a doctor with one
  warning and one failure) and a first-run machine with nothing on it and no CLI installed.
  "Empty versus populated" is a thing a stop declares rather than a teardown that must
  un-populate a list.
- **The real loaders, over seeded files.** `TeamCatalog`, `ForgeSessionCatalog`, the file-backed
  history and profile stores, the physical settings, directory and target probes — all of them,
  pointed at the sandbox. Only the process boundary is doubled, because it has no other seam.
  `ForgeSessionHydrator` rebuilds a whole wizard session from five JSON files, so the Composer,
  the mount rows and both verdicts are photographable with **no child process anywhere**.
- **The catalogue is data, and it is asserted.** Each stop declares where it stands, what it
  arranges, why that state is worth a pixel, and which ViewModel gates it claims to light up.
  The whole campaign is replayed headless on the Linux runner and every claim is checked — so a
  stop that stops reaching its state fails the build instead of writing a confident picture of
  the wrong screen. Writing those assertions immediately caught four wrong claims, including a
  stacked-modal state this application does not have.
- **Conformity guards**, in the idiom of the suite's existing ones: every sidebar entry maps to a
  capture screen, every scrim modal is opened by some stop, every endless storyboard has a pose,
  every guided-tour step names an element that exists.
- **Output that cannot lie.** Per-stop fault barrier; geometry, non-uniformity, expected-panel
  and binding-error checks; a differs-from-the-previous-shot digest that catches the likeliest
  failure of all, a stop that changed nothing; atomic writes; failed shots quarantined under
  `failed/`; and a `manifest.json` carrying each image's reason, its SHA-256 — so re-running the
  campaign after a UI change and diffing two manifests names the exact screens that moved.

### Fixed — five defects the campaign was hiding

Found while making the collection trustworthy; each produced, or was about to produce, an image
that looked like evidence.

- **The startup plate would have bled into every shot.** `SkipSplashForCapture` ran a
  zero-duration animation and set `Visibility` from its `Completed` handler — and a zero
  *duration* is not a synchronous completion: `BeginAnimation` attaches the clock and the media
  context ticks it on the next render pass. It worked only because the settle slept 120 ms
  afterwards. Replaced by a direct pose/hide pair, alongside a `PoseSplashForCapture` that
  photographs a plate a user could actually have seen instead of five pixels of progress bar.
- **The theme button contradicted its own window in dark mode.** The icon and tooltip were
  refreshed only from the toggle handler, so applying the theme any other way left a moon in the
  title bar — the first place a reviewer looks. `ApplyThemeForCapture` does both, and
  deliberately does not touch the operator's stored preferences.
- **The language menu could never appear.** It is the app's only `Popup`, hosted in its own
  `HwndSource` and therefore invisible to `RenderTargetBitmap`: its shot would have been a
  chevron rotated to 180° above nothing. Popups are now composited into the window's image, with
  the computed placement asserted inside the window's bounds rather than trusted.
- **The settle proved nothing.** WPF orders `Loaded` below `Render`, so the second dispatcher
  fence returned immediately and `Task.Delay(120)` was the only thing creating slack. Replaced by
  a bindings drain, a layout loop that repeats until layout stops dirtying itself, bounded
  composition ticks and an idle drain — with an unstable layout recorded rather than ignored.
- **A failed shot was invisible and inflated the count.** `Save` returned silently on a
  zero-size window *after* the index had been incremented, and the count printed on stdout was
  the stop count, not the file count.

Also: the collection is captured at 1440×900 rather than 1024×768. At the old size the content
column is 752 DIP and the panels declare maximum widths of 880, 1000 and 1080 — so none of them
ever bound, and every image ever produced showed the narrowest layout the app can make, which is
not the one the mock was drawn for. `captures/` joins `.gitignore`.

### Fixed — the pre-push audit: what forty-one reviewers found in the campaign season's own commits

A six-dimension adversarial audit of the fifteen unpushed commits (token/cache accounting,
multimodal, dialect hooks, fleet coherence, documentation, commit hygiene) confirmed no
accounting defect and no payload defect — and a crop of real gaps at the edges, each fixed
test-first:

- **MiniMax, truncated mid-thought**: a reply cut by `max_tokens` inside the `<think>`
  block never reaches the closing tag, and the split hook shipped the whole raw trace as
  visible content — the exact failure the hook exists to prevent, on the exact path where
  the model is most verbose. An unterminated leading block now yields empty content with
  the partial trace in `reasoning_content`. The hook's edges are pinned while at it: a
  mid-content `<think>` survives (quoting is content), an empty block records nothing, and
  the stream-final scrub is now a test, not a hope.
- **The streaming chat path dropped images silently**: `ChatStreamingAsync` built the same
  payload as the buffered path without calling `WarnOnUnsendableAttachments`, so a
  non-vision provider streaming a multimodal conversation lost the image without a word —
  the guarantee depended on which transport the caller picked. Both paths warn now.
- **Ollama's URL-only images**: the converter documents that an image referenced only by
  URL is "reported as skipped rather than silently dropped" (the server never fetches
  URLs), but nothing reported it. `BuildChatPayload` now emits the structured warning with
  the remedy (inline the bytes).
- **The None warnings claimed too much**: "this API has no response-format field" is
  factually wrong for MiniMax — the only provider that triggers it — whose API accepts the
  field and ignores it; same for "no reasoning pass" on a model that always reasons. Both
  texts now state the honest diagnosis (not honoured / no control), and MiniMax's two
  uniquely reachable warning branches are under test.
- **Studio called the mainland MiniMax "custom"**: the detector mapped only
  `api.minimax.io`; `api.minimaxi.com` — a constant this very range introduced, routed by
  the runtime factory — now detects as `minimax`, the Kimi twin pattern.
- **`llm.minimax`**: the scripting namespace gained the accessor (binding, provider-name
  switch, typings, PublicAPI), as `llm.grok` had — a first-class provider should not be
  reachable everywhere but from a script. `llm.grok` gets its first pin alongside.
- **The registry's promise is now kept in print**: "the report header prints the values
  actually used" was false for the M7 effort (`mistral-medium-2604` probes at `high`, the
  header said nothing) — the probe report (markdown + JSON) and both kit report writers
  now print the base and M7 efforts whenever a run departed from the default, and
  `--thinking-effort` is a real flag of both campaign scripts (explicit beats catalogue),
  as their options table already claimed.
- **Documentation swept against the code at HEAD**: the fleet is 14 everywhere (eight
  pages EN+FR still said 13, one said twelve); the response-format matrix lost its stale
  contradictory `Gemini | None` row and gained the missing MiniMax row; the vision table
  no longer lists DeepSeek as both having and lacking the capability and counts all 14;
  the opt-in page's "12 of the 13 declare it (only DeepSeek does not)" was wrong on both
  halves; every "campaign pending / non campagné / NOT yet campaign-verified" left over
  from MiniMax's documentation-first landing now states the campaign; `--m7-effort`
  reached the CLI reference; the Grok custom-endpoint report's paste-ready journal line
  named a path that does not exist; and this file's own `max_completion_tokens` entry
  claimed Groq passed a campaign two sections after recording that Groq never ran one.
- **Pins the audit found missing**: Gemini joins the fleet capability table (its
  declaration was the only one unpinned), and the `xai-` key-prefix inference — the last
  routing arm — gets the test that distinguishes it from a deleted one.

### Added — MiniMax, the fourteenth provider — the first documentation-first integration

Unlike Grok, which arrived preceded by its own live campaign, MiniMax arrives with no key
and says so everywhere: `MiniMaxLlmProvider` (OpenAI-compatible, `api.minimax.io/v1`
international with the `api.minimaxi.com` mainland twin named, the Kimi pattern), canonical
key `minimax`, default model `MiniMax-M2` — every declaration sourced from the vendor's
platform documentation dated 2026-08-30 and marked campaign-pending in the code, the
comparison table (a † row), and the campaign catalogue. `response_format` and thinking stay
undeclared (capability warning, never a silent drop — the Gemini precedent, upgradeable the
day a key arrives); vision follows the documented VL family per D-03. The catalogue entry
records the three questions the first campaign must settle: DeepSeek-style reasoning
replay, cache breakdown, and whether `response_format` works despite being undocumented.
Full fleet integration otherwise: factory routing (key, both regional hosts, `minimax-*`
model prefix), Studio card and detection, doctor, `orkeon llm probe|models`, example
settings, counts 13 -> 14 under the claims gate.

### Fixed — MiniMax speaks its mind out loud, and the dialect now separates the two

The first MiniMax campaign (same day as the integration — 7/2/3 on `MiniMax-M2`, once the
right key arrived: `sk-cp-` keys are coding-plan quotas, `sk-api-` is the API) settled all
three recorded questions and found one real integration defect. MiniMax ships its reasoning
INLINE: every reply opens with a `<think>...</think>` block inside `content`, no separate
field — a one-line hello came back as 158 characters, and an agent built on it would speak
its private reasoning out loud. The dialect now splits the block into `reasoning_content`
(a `SplitReasoningFromContent` hook on the compatible base — the MiniMax twin of the
Mistral chunked-content fix) and re-inlines it verbatim when replaying an assistant turn,
because the vendor documents that history must keep the think blocks. Post-fix re-campaign:
a 24-character hello. The other two answers: `response_format` is accepted but NON-BINDING
(a schema is ignored, `json_object` arrives fenced in markdown — the None declaration
graduates from caution to measurement), and the implicit cache reports no breakdown at an
8k prefix. The standing reds are the model's: system message ignored on both shapes, and
"I'm unable to view the image" — text-only per model (D-03), with the VL family absent from
the platform's `/models` listing, so no vision companion is declarable yet.

### Removed — Groq, superseded by Grok (breaking, no shims)

Groq was never the intended provider: the near-homograph had stood in for Grok since the
provider list was first drawn. With Grok landed and Groq never campaigned (no key ever
supplied, no archived proof), the fleet drops it outright per the pre-release rule - the new
state replaces the old one everywhere, no aliases, no shims. Gone: `GroqLlmProvider`, the
satellite constants (endpoint, key, default model), factory routing (key, groq.com host,
mixtral/groq model inference), the Studio card and detection, the scripting DSL factory
(`llm.groq` becomes `llm.grok`, which also retires its stale hardcoded llama-3.1 default),
the doctor mapping, the campaign-kit entry and the docs rows. The HuggingFace `:groq`
routing suffix stays - that is HF's partner vocabulary, not Orkeon's provider key. Provider
count returns to 13; the claims gate re-derived it and named every count to fix.

### Added — Grok (x.AI), preceded by its own proof

The key supplied for "Groq" turned out to be an x.AI key (`xai-` prefix, refused by
api.groq.com, served by api.x.ai) — and the user's intent turned out to be Grok all along.
Before the provider existed, a full 12-mode campaign had already passed against `api.x.ai`
through the generic OpenAI dialect with nothing but a base-url (archived under
`llmproviders-test/custom-endpoints/`), so `GrokLlmProvider` is that measurement written
down: OpenAI-compatible transport on `api.x.ai/v1`, `json_schema` honoured, effort-only
thinking with the trace replayed, vision, implicit cache on the standard `cached_tokens`.
Canonical key `grok` (alias `xai`), default model `grok-4.6` (verified live), endpoint and
key and default in the `Orkeon.Constants.Llm` satellite, factory routing by key, by
`api.x.ai` host, by `grok-*` model prefix and by the `xai-` key prefix — the grok/groq
near-homograph is exactly the confusion that last inference absorbs. Studio gains the
provider card and endpoint detection; `orkeon llm probe|models` accept it; the campaign kit
carries its catalogue entry; docs and counts follow everywhere the claims gate checks.

### Fixed — what the first real campaigns against six vendors found (2026-08-30)

The first full campaigns ever run against api.openai.com, the Gemini compat surface and an
identity-linked Anthropic key — plus re-runs of DeepSeek, Kimi, Z.AI and Ollama on rc.2 —
turned five findings into fixes. Each one is dated and pinned by an offline test:

- **OpenAI: `max_tokens` is retired on current models.** The campaign failed ten modes out of
  twelve on `gpt-5.6-sol` over that one field (`"Unsupported parameter: 'max_tokens' ... Use
  'max_completion_tokens' instead"`). The OpenAI dialect now writes `max_completion_tokens` —
  verified live to be accepted by the older generations too (`gpt-4o-mini`) — through a
  `MaxTokensFieldName` hook on `OpenAICompatibleProviderBase` that only `OpenAIProvider`
  overrides: DeepSeek and the rest of the compatible family still document and expect
  `max_tokens`, and every provider campaigned that day passed with it.
- **Gemini: `response_format` works and was being refused.** Declared `None` when the compat
  surface left it undocumented (2026-08-18), so every JSON request got a capability warning
  instead of being sent. Measured live: the surface accepts `json_object` and `json_schema`
  and enforces the schema server-side (`additionalProperties` included). The declaration is
  now `JsonSchema`.
- **DeepSeek: vision arrived, per model.** `deepseek-v4-flash-vision-exp` reads a base64
  image (measured live); DeepSeek was the one provider in the fleet with no vision model and
  its images silently degraded to text. `Vision = true` now, with the same per-provider
  declaration / per-model reality the fleet already lives with (D-03): the text-only default
  model answers an image with the vendor's own error.
- **Anthropic: identity-linked API keys were unusable.** They refuse every request without an
  `anthropic-workspace-id` header — Messages API and `/v1/models` alike — and Orkeon had no
  way to send one. `LlmConfig.WorkspaceId` (a scoping identifier, not a secret, same family
  as `ApiVersion`) now travels as that header when set; classic keys change nothing. Wired
  through `orkeon llm probe --workspace-id` and the campaign kit (`workspaceId` per provider).
- **Ollama: every buffered completion was accounted as free.** The buffered
  `/api/generate` parse hard-coded `TokensUsed = 0` behind a comment claiming Ollama
  provides no count in that format — while the live server returns `prompt_eval_count`
  and `eval_count` right beside the durations the same parse was already reading. Zero
  fed the token dimension of `AgentExecutionBudget` and the crew accounting. Found by
  the M1 probe archiving `tokens=0` on a priced exchange; the same gap left M10's
  diagnostic line reading "prompt tokens: unreported" for Ollama, because a tool-less
  conversation flattens onto that very path. Both now report (`tokens=35`,
  `prompt tokens: 2052 then 2052` on the re-run).
- **The probe harness accused the framework twice, wrongly.** M5 rebuilt the assistant
  tool-call turn from parsed calls while the real agent loop replays the vendor's raw
  `tool_calls` fragment verbatim — Gemini rejects a replay that lost its per-call
  `thought_signature`, so the probe failed M5 for a defect the product does not have; it now
  replays the raw fragment whenever the body is OpenAI-shaped (the canonical rebuild remains
  for Anthropic's dialect). And M3/M4 asked for five numbers, which fits in a single event on
  a coarse-chunking stream (Gemini emits ~13-character chunks), reading a genuine stream as a
  buffered fallback; the probe now demands a hundred — thirty sufficed for Gemini,
  then claude-sonnet-5 coalesced the whole count-to-thirty into a single delta the
  same day.

The Mistral default model was broken and nothing could have said so offline:
`LlmProviderDefaultModels.Mistral` carried `mistral-medium-3-5-26-04`, an identifier the API
never served (`Invalid model`, measured 2026-08-30) — it reads like a concatenation of the
alias `mistral-medium-3-5` and the vintage `2604`, the two forms Mistral really serves. Any
configuration naming the provider without a model got a guaranteed 400. Now
`mistral-medium-2604`, the dated snapshot, verified live; same class as the retired defaults
LLM-01 fixed (G-01..G-04), found the same way — by the first real call.

The mandatory values some models dictate are now a per-model registry, not folklore:
`requiredParams` in the campaign catalogue (kimi-k2.6, gpt-5.6-sol and claude-sonnet-5 all
refuse any temperature but 1; gpt-5.6-sol additionally demands `reasoning_effort: "none"`
with function tools), resolved per model by both campaign scripts — per model and not per
provider, because `gpt-4o-mini` rejects `reasoning_effort` outright and a provider-level pin
would break it. Human-readable twin: the "Per-model mandatory parameter values" section of
`docs/reference/llm-providers-comparison.md` (mirrored in French), vendor wording and
measurement date included.

Mistral, first campaign ever (the key arrived last), peeled four findings in a row before
settling at 11/1 on `mistral-medium-2604`, with the G-14 proof in the header
(`api.mistral.ai`, not `localhost`, no base-url passed):

- The compiled default was the broken identifier above — first finding, fixed first.
- The model restricts `reasoning_effort` to `['high', 'none']`, so M7's hard-coded "low"
  was refused: the probe's effort is now a per-model registry value (`m7Effort`,
  CLI `--m7-effort`).
- **Orkeon's omit-top_p-when-1 was a silent drop there.** Mistral's reasoning mode runs an
  internal top_p default of its own and validates greedy sampling against the EXPLICIT
  field, so `temperature: 0` + reasoning with no `top_p` is refused (`"top_p must be 1 when
  using greedy sampling."`) while the same request with an explicit `top_p: 1` passes. The
  Mistral dialect now always writes the configured value (`AlwaysEmitTopP` hook — the base
  keeps omitting: OpenAI's reasoning models reject the explicit field, the same assumption
  broken in the other direction).
- **Reasoning replies were parsed as empty.** With `reasoning_effort` on, Mistral answers
  `message.content` as an ARRAY of typed chunks (`thinking` + `text`) and the string-only
  read dropped both: M7 archived `accepted, no reasoning trace returned, tokens=243` — 243
  tokens billed, nothing kept. The shared parse now walks the text parts (the OpenAI
  multi-part standard) and surfaces the thinking chunks as `reasoning_content`; the re-run
  archives `reasoning trace returned`.

The one standing red is the vendor's: `prompt_tokens_details.cached_tokens` stays 0 on an
identical 5812-token prefix called twice (three measurements) — the field exists, the
implicit cache never hits.

Qwen, first campaign (an international key: `dashscope-intl` base URL, G-12 closed by the
report header): 12/12 — including the fleet's only `Thinking = Budget` wire
(`enable_thinking` + `thinking_budget`) validated live. Its M10 exposed a harness artefact,
not a vendor gap: DashScope's implicit cache hits nothing below a high threshold (0 cached
tokens at the probe's historical ~2050-token prefix, three runs; `cached_tokens: 4352` at
5809), so a working cache read as broken. The M10 prefix now carries ~8200 tokens — the
clean run archives 6528 cached, ratio 0.81; Mistral's zero stayed zero at the long prefix
too, which settles its verdict as the vendor's. And the OpenAI-dialect fallback for custom
compatible endpoints — the path Docker Model Runner, vLLM and LM Studio ride — got its
first real proof: a full 12/12 campaign against `api.x.ai` (`grok-4.6`), archived under
`llmproviders-test/custom-endpoints/`. The key supplied as "Groq" was an x.AI key
(`xai-` prefix, refused by api.groq.com, served by api.x.ai) — measured, not assumed.

Together and HuggingFace, first campaigns (the last two keys): 9/1/2 and 8/2/2 — no Orkeon
defect on either. The one harness defect was found before the campaigns could even start:
Together answers `GET /models` with a bare JSON array, no `data` envelope, and the first
real call crashed `orkeon llm models` with an unhandled `InvalidOperationException` — the
parser now reads both shapes and the command's catch treats a malformed body as a typed
error (red test first, on the vendor's verbatim shape). The oldest suspicion of the effort
is settled by measurement: HuggingFace's compiled default `meta-llama/Llama-3.1-8B-Instruct`,
long flagged unreachable through the router, answers M1 alive — the campaign catalogue
realigns on it. Both M9 reds are the D-03 pattern; HuggingFace's vision companion
(`Qwen3-VL-30B-A3B-Instruct`) reads the image, while Together's four vision candidates are
all non-serverless on the measured tier (dedicated-endpoint only, failure archived as proof).

Campaign verdicts, same day: Kimi's open M3 "stream refused" of August did not reproduce
(4/4 green, replays included); Z.AI's implicit context cache missed once in-campaign
(0 cached tokens) and hit on both replays (1984 tokens, ratio 0.97) — server behaviour, not
a defect. Reports under `llmproviders-test/`.

### Fixed — `dotnet test` runs again, and the tests it skipped are back

The .NET 10 SDK stopped honouring the VSTest target for Microsoft.Testing.Platform test
projects. Every `dotnet test` in this repo answered *"Testing with VSTest target is no longer
supported by Microsoft.Testing.Platform on .NET 10 SDK and later"* and ran nothing — CI, the
release pipeline, the nightly and the SonarQube script alike. `global.json` now opts into the new
runner (`"test": { "runner": "Microsoft.Testing.Platform" }`), which is the only supported answer;
the per-project `TestingPlatformDotnetTestSupport` property no longer does it.

Three things follow from the new runner, and each was a live defect rather than a rename:

- **A filter matching zero tests in a module is an error there** (exit 8), not an empty success.
  CI excluded `Orkeon.Tools.Embeddings.Local.Tests` by name from a solution-wide filter, which
  under MTP means "load that module, match nothing, fail". The exclusion is gone: measurement
  showed the SIGSEGV that motivated it comes from that assembly's **8 `Category=Slow` tests** —
  the ones that boot the real ONNX model — and from those alone (3/3 crash with only them, 5/5
  clean without them). Its other 28 tests now run with everyone else's instead of being skipped,
  and the 8 get a step whose guard reads the runner's own accounting: the crash is tolerated only
  when every discovered test was accounted for and none failed, so a run that dies mid-suite stays
  red. The previous guard accepted that case.
- **The nightly needs the opposite tolerance.** Selecting one category across the whole solution
  leaves most modules empty by construction, so it passes `--ignore-exit-code 8` — plus an
  explicit check that the run executed something, because tolerating "zero tests" per module must
  not let "zero tests anywhere" look green. Its VSTest-only `--logger` argument is gone.
- **Coverage was measured by a collector the runner does not implement.**
  `--collect:"XPlat Code Coverage"` is refused outright (exit 5), `scripts/sonar-analyze.*`
  swallowed the failure with `|| log_warn`, and the analysis went on to import a report nothing
  had written — a Quality Gate evaluating coverage conditions against 0% and reading as a
  measurement. Collection moves to `dotnet-coverage` (Cobertura → SonarQube generic via
  ReportGenerator, in both the shell and PowerShell scripts), and a report holding no class now
  **aborts** the analysis instead of being imported.

One flaky test surfaced with the new scheduler and is fixed rather than tolerated:
`OtelTests.llm_call_emits_a_span_with_method_and_prompt_length_tags` took the first LLM-call span
it saw from a process-global `ActivityListener`, so under a parallel run it asserted on a
concurrent `act()` loop's span. Its three neighbours already discriminated by a unique tag; it now
does too.

### Fixed — BM25: the guard the code index never had, and two comments that promised too much

`Bm25CodeIndex` (Analysis) accepted any `k1`/`b` through its public constructor. A negative `k1`
inverts the term-frequency saturation and a `b` outside `[0, 1]` turns length normalization into
an unbounded multiplier: the index kept answering, with scores no caller could interpret and no
error to notice. It now validates exactly as its prose twin `Bm25Index` (Rag) always did.

The two implementations' doc comments claimed they were kept in step — *"same k1/b defaults"*,
*"Same k, same semantics"*. Nothing enforced it, and on the fusion constant it was already false:
the RAG side is operator-tunable through `Orkeon:Rag:Retrieval:Hybrid:RrfK` while code search
exposes no such knob. The comments now say what is true — the values come from the literature, the
two indexes never score the same corpus, and retuning one must not propagate. ADR-009 records why
these are not satellite candidates: a shared spelling is not a shared value.

The one real duplication there was internal to RAG. `ReciprocalRankFusion.DefaultK` and
`RagDefaults.RrfK` were two declarations of the same product default, backing two option classes
bound to the **same** configuration section (`Orkeon:Rag:Retrieval:Hybrid`) from two projects — so
changing the default would have moved one fusion and not the other. `Orkeon.Rag` already
references the Domain that holds it; the copy is now a reference, and the published value is
unchanged.

### Added — satellites for the constants two projects must agree on (ADR-009)

`Orkeon.Studio.Core` may not reference `Orkeon.Infrastructure` or `Orkeon.Hosting`, and the
reason is measured rather than doctrinal: doing so *"dragged the whole runtime — ONNX runtimes,
tree-sitter grammars, the local embedding model — into every published app, for roughly 230 MB
each"*. So Studio copied the values it needed by hand, and `ConstantDriftTests` existed to stop
the copies diverging.

Five new packages hold them instead, each with **no runtime dependency**, which is what lets
both sides reference one declaration: `Orkeon.Constants.Llm` (provider endpoints, default models,
provider keys), `Orkeon.Constants.FileSystem` (the virtual roots a runner mounts for itself, and
the conventional file names one component writes and another looks for),
`Orkeon.Constants.Configuration` (`Orkeon:*` keys and shared operator wording),
`Orkeon.Constants.Protocol` (the run event kinds a runner emits and Studio reads) and
`Orkeon.Constants.Cli` (the run option names the runners accept and Studio predicts).

Two of those closed drift that had already happened rather than drift that might: Studio's copy of
the run event vocabulary was missing four kinds the runner emits, and an unknown kind is ignored
rather than reported — so tool activity and delegations simply never reached the screen.

`Orkeon.Domain` gains its first runtime project reference, to `Orkeon.Constants.Llm` — its
default model name is the same string as OpenAI's provider default. A project that depends on
nothing inverts no layer and drags nothing in behind it; ADR-009 records the argument.

Nothing published changes shape: `LlmEndpoints`, `ProviderDefaults` and `LlmDefaults` are frozen
surfaces, so their names stay and only their value moves — a `const` initialised from another
assembly's `const` is inlined at compile time. Only `RunnerMounts`, which was unshipped, is
removed; its four roots are now `RunnerVirtualRoots`.

`ConstantDriftTests` goes from 12 facts to 5. The five that remain never guarded duplication:
they ask the endpoint detector to recognise each endpoint, pin the Docker Model Runner defaults
against the committed `appsettings.json` template, and check the declared minimum CLI version.

Publication order matters now: `.github/workflows/publish.yml` pushes the satellites before
`Orkeon.Domain`, or Domain would ship with a dependency that cannot be restored.

### Changed — build toolchain

- **Roslyn pinned to 5.9.0 repo-wide.** The analyzer and the source generator reference
  `Microsoft.CodeAnalysis` 5.9.0, and a Roslyn component that references a newer compiler than the
  one running it is *silently refused* — a warning (CS9057), not an error, after which analysis and
  generation simply do not happen. `Microsoft.Net.Compilers.Toolset` now replaces the SDK's `csc`
  so both are actually loaded. It is `PrivateAssets="all"`, so it never flows into a package;
  a consumer on an older SDK still gets the CS9057 behaviour, which is why the components declare
  the floor they need rather than the newest compiler available.
- **xunit v3 → v4.** Test projects move to Microsoft.Testing.Platform. `dotnet test` in its VSTest
  mode no longer drives them.
- **Jint interop pinned to `ArrayConversionMode.Copy`.** Jint 4.14 changed the default for CLR
  arrays to `LiveView`, under which `Array.isArray` on a returned array is **false**. The scripting
  DSL's typings promise `readonly number[][]` from `llm.embed()`, so a script branching on
  `Array.isArray` silently took the wrong path. The pin restores the behaviour the typings
  describe. Note the scope honestly: this governs CLR *arrays*, so a binding returning
  `IReadOnlyList<T>` is host-wrapped under either mode and `Array.isArray` stays false for it.

### Fixed — a team associates a folder, it does not declare one

In Orkeon Studio, « Autoriser un dossier » on a team opened the folder picker:
a disk tree, a physical path, a rights choice. That is the **declaration**
screen, and it belongs to « Réglages › Dossiers autorisés » — nowhere else.
Wired onto the two team screens, it made every team re-declare its mounts from
scratch, next to a settings list that already held them.

The defect was two lines of shell wiring, not the picker: `MainWindowViewModel`
had all three callers subscribed to the same modal. The two team gestures — the
creation wizard's « Dossiers de cette équipe » block, and « Autoriser un autre
dossier… » on an adopted team — now open a chooser over the folders the
settings declare. Ticked entries are carried over **verbatim, rights included**:
the settings are the one place a folder and its rights are decided, and a team
able to widen them would make that declaration a suggestion. A folder the team
already carries, or whose virtual root another folder already spends, says so
and cannot be picked — two mounts on one root is not a merge the runtime
performs, it is one it drops.

Declaring stays the settings' gesture, and « Déclarer un nouveau dossier… » is
one door to it: the modal closes and the app lands on « Réglages › Dossiers
autorisés », on that tab and not merely on that screen. One door, so a folder
cannot be declared from two places and drift between them.

A team folder the settings do **not** declare now reads red — on the wizard's
chips, the "Mes équipes" cards and the team-mounts modal alike. It is not an
error: a team's `/output` and `/input` are created inside the team at adoption
and are never declared. It is the one thing a row cannot say by naming a virtual
path, and a team reaching outside the machine's authorized folders should not
have to be discovered by reading a sidecar.

A team reaching outside the settings no longer launches. « Exécuter » refuses a
team carrying a folder that no settings entry allows: the run button is disabled
and the card names the folders and the two ways out, with a button onto
« Réglages › Dossiers autorisés ». Discovering that refusal from a run that
failed halfway, its reason buried in a log, is what this replaces. The rule lives
in `Orkeon.Studio.Core` (`DeclaredMounts.BlockingFolders`), not in the WPF
screens, so the TUI launcher cannot answer it differently. A team's own `/output`
and `/input` never block it: they are created inside the team at adoption and are
its own plumbing, and counting them would make every adopted team unlaunchable.

The folders the blueprint implies became removable like any other. They were
informative chips with no ✕ — "edit an agent to change them" — which left a team
carrying a root its owner did not want with no way to say so. Dropping one now
sticks: `WithDerivedWriteMounts` no longer re-adds it, the same silent undo that
method exists to prevent. The screen warns and names the dropped roots, because
nothing will be bound to them and the agents writing there will fail; a single
« Rétablir » is the way back from a wrong ✕.

### Fixed — Virtual paths are the only currency agents are paid in (ADR-008)

The owner found absolute disk folders in Orkeon Studio where only VFS mount
points belong. The leak was in the engine, not the UI.

Runners used to mount their own directories **1:1** (`C:\x:C:\x:ro`) so the
absolute paths framework code had already computed resolved unchanged, and
`FileSystemMount.IsValidVirtualPath` had been widened to accept a Windows drive
path as a *virtual* path to let those strings parse. Mounts parsed from a mount
string are agent-facing, so `list_mounts`, `ShellCommandTool`'s path checks and
every access-denied message — which names the available mounts — handed agents
the operator's disk layout; redaction was explicitly disarmed for exactly those
mounts. The crew directory is now `/crew`, a hosted daemon's crews `/crews`,
`/crews-1`, …, and the loader receives the virtual spelling, so it asks the VFS
whether its target is a directory instead of probing the disk.

`Orkeon:FileSystem:InternalMounts` is new: mounts registered with
`MountVisibility.Internal` — reachable by the VFS, absent from
`GetAvailableMounts()`. The `--llm-log` directory moves there; it holds full
prompts and API payloads and was being advertised to every agent as a writable
mount. As a result, turning `--llm-log` on no longer shifts
`Orkeon:FileSystem:Mounts:{i}`.

A denial message is read by the LLM, so it is an agent-facing surface like
`list_mounts`: `FileSystemRegistry` now builds its "Available mounts" and
"Mounts granting Write" lists from the agent-facing mounts only. It used to
enumerate *every* mount, which named `/llm-logs` to any agent that touched an
unmounted path — and annotated it `(writable)`.

In Studio, the agent editor's « Sur quel dossier » line and the Composer's
folder chips now name mounts the way agents address them (`/output (lecture,
écriture)`) instead of joining raw `physical:virtual:rights` strings. Expert
surfaces — the effective-mounts table, the picker's preview — still show the
exact command line.

**An adopted team now writes where its own agents write.** The trial bench
mounts `/output` and refuses a run that did not produce its promised
deliverable; nothing carried that mount further, so the same team launched from
its own folder was denied `/output`, logged a warning and reported success with
nothing written. `forge promote` derives the write roots from the blueprint,
creates their folders, and spells them in both launchers; Studio binds the same
roots into the sidecar at adoption. A deliverable root the runner reserves —
`/crew`, `/script`, `/llm-logs` — and the traversal spellings `/..` and `/.` are
skipped rather than mounted.

**And the promoted launchers now run from the team's own folder.** Neither
`run.sh` nor `run.cmd` changed directory, while both address the crew by an
absolute anchored path: the runner refuses to read a crew outside the working
directory without `--allow-external-mounts`, and the security whitelist is
rooted on the working directory too. Launched from anywhere else — which is
every scheduled run the generated `schedule/` artifacts install, since a service
starts in the system directory — the team was refused outright, or ran and wrote
nothing. Both launchers now `cd` into their folder first. In the same file,
`--var` was spelled once per sample variable, which the CLI's parser rejects as
a repeated option: several values now go space-separated after one flag, the
rule `--mount` already followed.

**Breaking**: `--mount` no longer accepts a drive-letter virtual path, and a
`--mount` claiming a root a runner reserves for itself — `/crew` and
`/llm-logs` on the YAML path, `/script` and `/llm-logs` on the scripting path,
`/crews*` on the `orkeon-host` daemon (exit 78) — is refused with an actionable
line.
`RunnerHost.Build`'s `llmLogPath` parameter becomes `llmLogVirtualPath` and
takes a virtual path; a new optional `internalMounts` parameter follows it.
`RunnerExecution.LoadCrewAsync` keeps its signature but changes contract: its
`configPath` is now a **virtual** path, so a host passing a physical one is
denied by the VFS instead of loading. `RunnerExecution.EnsureReservedRootsAreFree`
is public, for hosts that inject mounts of their own.

**A mount string can quote its physical path.** Three call sites split a spec
on `:` with three different heuristics, and the one in
`CliWorkspaceMountBootstrapper` had none: on Windows it read
`C:\src:/workspace:ro` as the physical path `"C"`, resolved it against the
working directory and emitted a corrupt mount string. The split now lives once,
in the domain type — `FileSystemMount.TryGetBasePath`, `WithBasePath` and
`Quote` are new, and both duplicate heuristics are gone.

A path the bare form cannot carry — one holding a `:` or a `;`, or ending with a
backslash — is **quoted**: `"/data/odd:name":/data:ro`, `"C:\src\":/workspace:ro`.
Quoting rather than backslash-escaping, because a backslash escape would collide
with the Windows path separator, which is exactly what has to survive here.
Backslashes are ordinary characters, so every existing mount string is
unchanged, and two folders that had no spelling at all now have one: a drive
root (`"C:\":/workspace:ro`) and any path ending in a separator. Those quotes
belong to the mount grammar, so a shell must be told to leave them alone — the
CLI reference spells the bash and PowerShell forms.

Every producer goes through `FileSystemMount.Quote`, not just the parsers
through the split: the runners' own crew / script / exchange-log mounts, the
daemon's crew mounts, the two `orkeon-repl` bootstrappers and Studio's
`MountDefinition.ToMountString`. A folder whose name holds a `;` is legal on
every OS Studio's picker browses, and each of those sites used to emit a spec
its own parser then refused.

**And an adopted team can be launched from its own card again.** Studio's target
detector looked for a crew definition at the root of the folder it was handed,
while `forge promote` keeps it in `crew/` — so « Lancer » on a team card
answered *"holds no crew definition… pick a file inside it instead"*, and
picking `crew/` by hand found no sidecar beside it, so the team's mounts never
reached the command line. The detector now descends into `crew/`: the run path
goes to the definition, the *selected* path stays the team folder, which is what
puts the sidecar in view and the team's own `/output` inside the security root.

Two more Studio screens stopped showing folders: the « Importer une équipe »
recognition report joined the sidecar's raw mount strings — read by whoever
*received* the team, so it disclosed the exporter's disk layout — and the
team-mounts modal fell back to the raw string in a field documented as "the
virtual spelling the agents see". All four renderers now share one
`MountLabels`.

Also fixed: two hosted crews sharing a `Name` resolved to different definitions
(the plan kept the last, `CrewHostRegistry.Find` answers with the first);
`examples/service-host/appsettings.host.json` declared its mounts as objects, a
shape `FileSystemOptions.Mounts` cannot bind; the RaggableTree pages documented
a mount syntax that does not exist; and the `--mount` / `--var` help text said
"Repeatable." of options the parser refuses to see twice.

### Fixed — the VFS boundary, asked the same question everywhere

A sweep over the code the ADR-008 diff did not touch, looking for the same
shapes it had just corrected.

**One containment predicate.** "Is this path inside that directory?" was
answered in three places with three rules. `PathValidator`'s was boundary-safe;
the runners' `--allow-external-mounts` guard was a bare `StartsWith`, and the
two disagreed exactly where it hurts: a crew in a sibling folder whose name
extends the working directory's (`~/proj` vs `~/proj-old`) read as *inside*, so
the opt-in was never demanded, its base path never whitelisted, and the
boundary-safe validator then refused every file the crew touched — never naming
the flag that would have fixed it. `PhysicalPathContainment` now holds the rule,
including the part about case: Windows paths are the same path in any casing.

**`ToVirtualPath` answered with the wrong mount.** The registry orders its
mounts by *virtual* path length, which is what the virtual→physical direction
needs; coming back, it returned the first mount whose *base* path matched. With
nested mounts the two orderings disagree, and a file was handed back under a
parent mount's spelling — a name that re-resolves with different rights. The
most specific physical base now wins.

**`/sandbox` existed in one host out of all of them.** The mount the code
sandboxes write under was provisioned by an `IHostedService`, and the runners
build a host they never start — so every shipped CLI registered `ICodeSandbox`,
`DockerSandbox` and `SecureCodeInterpreterTool` over a virtual root that did not
exist. It is now built with the registry, in every host, started or not.
**Breaking**: `AddSandboxMount` is removed; `AddOrkeonFileSystem` does it.

**`shell_command` could not use its own default.** `working_directory` declared
`"."` — and since ADR-008 a virtual path starts with `/`, so no registry can
resolve it and every call that omitted the field, the shape a model writes for
an optional one, was refused. It now runs in the first readable mount, or in the
parent's directory when nothing is mounted.

**Every promoted team was dead on arrival on Windows.** The launcher built its
`--mount` by concatenation, so the physical segment inherited whatever the
anchor expanded to — and `%~dp0` is always `C:\…`. Four segments, a grammar
error naming a path the user never typed. The generated spec now carries the
grammar's own quotes, and a test runs a real shell against a folder whose name
holds the separator. The forge trial bench and Studio's mount prediction went
through the same concatenation; both now go through `FileSystemMount.Quote`.

**The daemon read its crew list from a file it never told anyone about.**
`orkeon-host` resolved a relative `appsettings.json` against the executable's
directory, while `--help` promises `./appsettings.json` and the host built
moments later reads the working directory — so the mounts came from one file and
everything else from another. It also accepted a malformed `--mount` at startup,
logged READY, and then failed every message; that is now a configuration error
with exit 78, and `--help` states the grammar the parser enforces (three
segments, `ro|rw|rwnd`, quoting).

**`--allow-external-mounts` overwrote the operator's whitelist.** It wrote
`PathSecurity:AdditionalAllowedDirectories:0`, replacing the entry an
`appsettings.json` declares there, while its own documentation says it
*additionally* whitelists. It now appends.

### Fixed — configuration that decided nothing

**`process: parallel` now honours `dependencies:`.** The mode ignored them: every
task started at once, so a final synthesis task ran against an empty context and
reported success on the nothing it had. The documentation said to use Sequential
or Graph instead — and **23 of the 30 shipped `parallel` examples declare
dependencies anyway**, which is the clearest possible statement of what the mode
is for. Tasks are now grouped into waves: everything whose dependencies are
satisfied runs concurrently, the next wave starts when they are done and reads
their outputs. A crew declaring no dependency is one wave — the previous
behaviour, unchanged. A cycle is refused, naming the tasks caught in it.

**`AgentSelectionStrategy` now selects something.** The option, its two real
strategies, `StrategyAgentSelectionService` and their DI wiring all existed —
and `IAgentSelectionService.SelectBestAgentAsync` had no caller anywhere in
`src/`, so setting `Embedding` changed nothing at runtime. `TaskAgentSelector` is
that call site: consulted for a task declaring no `agent:`, never overriding one
that does, degrading to round-robin with a warning when the embedding backend
fails. `FirstFit` — the default — keeps round-robin exactly as before.

**An unknown `process:` is refused instead of guessed.** A hand-rolled switch
fell through to Sequential, so `process: graf` ran a pipeline the author never
asked for. Both the YAML and the scripting paths now use `ProcessType.TryFrom`
and name the valid values. **Breaking**: a crew file with a typo'd `process:`
now fails to load instead of running as Sequential.

**A Graph or Autonomous crew is no longer reported as Sequential.** Three
hand-written `MapProcessType` switches restated the six-mode value object with
four arms each and mapped every other mode to `"Sequential"`. They are gone; the
DTOs carry the value object's own spelling.

**`LogStreamingExchanges` is honoured.** It was bound from configuration, offered
as a checkbox in both Studio surfaces, pinned by Studio's settings validator —
and read by no code, so turning it off still captured every streaming exchange.

**Removed**: `OrkeonConfig` and `OrkeonFeatureFlags`. Four documentation pages
presented them as the framework's predefined configurations (`Default`,
`Development`, `Production`); no production code read either, and no DI entry
point accepted one. A host composes its settings through
`AddOrkeonInfrastructure` / `AddOrkeonApplication` and its `appsettings.json`.

Also: the five DLP interceptors' summaries read as descriptions of what the
framework does ("ensure PII never appears in logs") when nothing invokes them —
they are an opt-in toolkit a host applies, as
`docs/reference/opt-in-subsystems.md` already said, and each class now says so
too. And `asyncExecution:` is documented for what it is: recorded on the task,
honoured by no orchestration mode.

### Fixed — Studio, and the team it hands over

**An adopted team can now read, too.** The trial bench mounts `/workspace` and
the Composer shows the chip; nothing carried it into adoption, so a team whose
agents use `file_read` passed its trial and could then read nothing — the exact
mirror of the missing `/output`. `forge promote` and Studio now bind it to an
`input/` folder **inside** the team, and FORGE.md says to drop the readable
files there. Not the team's own root: `--with-settings` puts an
`appsettings.json` holding API keys at that root, and a read mount over it would
hand them to any agent with a file tool.

**One chip per virtual root.** A root the user allowed a folder for was rendered
twice — once as a removable chip, once as an informative derived one — and
removing the removable one changed nothing at save, because the derived binding
silently took its place. The derived list now shows only the roots no explicit
choice claims, so removing a chip brings the derived one visibly back: the
screen says what the save will do. The `DerivedMounts` documentation claimed
"the sidecar never records them", which was false and was the root of the
confusion.

**Studio's TUI launcher started the CLI in the wrong folder.** It derived the
working directory from `RunPath`, correct only while that path's parent was the
folder the user picked — and the ADR-008 detector change made `RunPath` descend
into a promoted team's `crew/`. The fix had landed in the WPF launcher alone.
Both now read `RunTarget.WorkingDirectory`.

**Studio reported "custom" for Gemini** — the endpoint its own preset catalogue
writes. The drift test guarding the pair asserted the constant had been *copied*,
not that the detector recognised it; it now asks the detector.

**Studio's blueprint→mount derivation gained the guards the CLI's copy has.** A
deliverable naming a reserved root (`/crew`, `/script`, `/llm-logs`) or a
traversal segment reached an adopted team's sidecar through Studio and nowhere
else, producing a team Studio could launch and the runner refused at start.
`ForgeDerivedMountTests` pins the pair.

### Fixed — an internal mount is a boundary now, not a hiding place

ADR-008 introduced `MountVisibility.Internal` and said the limitation out loud:
the mount is withheld from every listing and stays **resolvable**, so an agent
that knows the name can address it. The names are documented. `/llm-logs` holds
every prompt and every API response of the run — one
`file_read /llm-logs/llm-exchanges-….jsonl` was the whole exchange history.

`FileSystemRegistry` now refuses an Internal mount by default, in both
directions: `ToVirtualPath` will not name one either, so a tool's
physical→virtual output rewrite cannot leak it. A refused internal mount is
reported exactly like a path that does not exist, and a nested one does not fall
back to its agent-facing parent.

The two components that legitimately write to an internal root — the LLM
exchange logger and the code sandboxes — ask for the new
`PrivilegedFileSystemAccess` by name. A distinct DI registration rather than a
flag on the interface everyone already holds: a tool cannot obtain it by
accident, and every holder is findable by searching for the type. A host that
wires its own `IFileSystemService` instead of calling `AddOrkeonFileSystem`
keeps exactly the behaviour it had.

**Breaking**: `FileSystemRegistry.ResolveAndCheckRights` and `ToVirtualPath`
take an optional `includeInternal` (default `false`), and `FileSystemService`'s
constructor an optional `internalAccess` (default `false`).

### Fixed — what the tests were not asking

**The shipped scripting typings did not parse.** `tools.d.ts` declared an index
signature as `const [name: string]: …`, which a TypeScript namespace cannot
carry and which is not a declaration at all — so the `tools` namespace this
package advertises was unavailable to every editor that loaded it. The only
guard over the typings asserted that certain substrings were present in the
rolled-up bundle, which cannot fail for that. Every `.d.ts` is now handed to
esbuild, the same front end that reads user scripts. `llm.d.ts` went with it:
it typed `llm.openai`/`anthropic`/`ollama`/`azureOpenai`/`groq` as non-callable
objects with a `name` property, while the runtime exposes them as factories
returning a config whose field is `provider` — every script typed against those
declarations got an error on the correct code.

**`docker build .` failed at restore.** The Dockerfile restated
`Orkeon.ConsoleApp`'s project graph as a hand-written COPY list that had drifted
to 10 of its 22 projects, plus one it no longer references. The list is gone;
the graph is read from the tree.

**Two runs started in the same second shared one exchange-log file.** The run id
was a UTC timestamp truncated to the second, against a class claiming "each
logger instance creates a unique file scoped to that run" — and its per-file
lock serializes writers inside one process, never across two.

**The embedding port blew up at startup instead of saying what was missing.**
`AddOrkeonVectorSearch` — which `AddOrkeonInfrastructure(configuration)` calls
unconditionally — registered `OpenAIEmbeddingProvider` **by type**, and its
constructor needs an M.E.AI `IEmbeddingGenerator<string, Embedding<float>>` that
only a host choosing local embeddings ever registers. MS.DI throws when a
registered service's own dependencies cannot be resolved, so
`GetService<OpenAIEmbeddingProvider>()` threw rather than returning null — which
put **both** graceful fallbacks (this one and
`DefaultEmbeddingProviderResolver`'s) behind an exception naming an interface no
operator has heard of, at container build rather than at first embed.
`UnconfiguredEmbeddingProvider` exists precisely to give an actionable message
deferred to first use; it is now reachable. The provider is built from the
generator when one is present and absent otherwise.

**`orkeon doctor`'s `onnx-reranker` check now looks.** It was a hard-coded `ok`
with a hard-coded detail, on the reasoning that the weights are embedded
resources of a package the CLI always references — true, and still not a check:
a trimmed publish or a renamed resource leaves the reranker broken and the
doctor cheerful. It opens the streams and reports the size.

**Removed**: `ImageHelper` (`Orkeon.Tools.Abstractions`) — no production caller,
and where it disagreed with the live `ContentConverter` it was the wrong one:
it declared SVG a supported image type, which no vision API accepts.
`CliFileSystemService` (`orkeon`) — no production instantiation, a
`ResolveAndValidate` that ignored its `requiredRight` argument entirely, and
prefix compares with no separator boundary.

Also: `AgentMapper` hardcoded `Status = "Active"` while the two handlers that
actually map an agent read `agent.Status` and never called it — it reports the
real status now, and the handlers go through it. The `Orkeon.Tools.FileSystem`
layering guard was a denylist of two names under a doc describing an allowlist
that was already false; it is an allowlist. Four `*_ShouldHandleEvent` tests
whose only assertion was `Assert.True(true)` are gone — their siblings assert
what the handlers produce. And the forge sandbox's stated write boundary now
matches its mounts (the session directory, not the `/output` folder inside it),
`asyncExecution:` and the crew `rag:` block say what they do, and the CLI
reference carries the caveat both getting-started pages already had about
`orkeon run <dir>/crew`.

### Fixed — the adversarial pass, turned on the sweep's own work

The sweep that produced the sections above was reviewed by agents briefed to
refute it rather than confirm it. The deletions held: nothing removed was
reachable, and the database security policy the removal was accused of dropping
was in fact the weaker of two copies — the surviving
`Orkeon.Tools.Data.Relational.DefaultDatabaseSecurityPolicy` blocks stacked DDL
after a benign `SELECT`, neutralises comment prefixes and gates `UPDATE` without
a `WHERE`, none of which the deleted one did. What did not hold was the work
*around* the deletions.

**An encrypted memory item kept its content and lost its identity.** The
decorator rebuilt every item through `MemoryItem.Create`, which mints a fresh
`MemoryItemId` and stamps `CreatedAt = UtcNow`, `AccessCount = 0`,
`LastAccessedAt = null` — fields no caller can pass. `SqliteMemoryRecord`
restores exactly those from storage on purpose; wrapping that provider in
`EncryptedMemoryProviderDecorator` threw the work away again, so an encrypted
long-term memory reported the moment it was decrypted as its creation time and
never accumulated an access count. Ageing and recency-ordering read wrong
values, and only with encryption switched on. The rebuild goes through
`MemoryItem.Restore` now, which carries identity and metadata over whole rather
than enumerating fields that can be forgotten. Consolidating the six inline
rebuilds into one place had fixed the two dropped metadata fields and asserted
completeness without checking identity.

**Removed**: `FeatureFlags` (`Orkeon.Application.Configuration`). Its twin
`OrkeonFeatureFlags` was removed above for having no production reader; this one
sat in the same folder with the same profile, and was the more misleading of the
two — `ShouldUseForAgent` / `ShouldUseForCrew` implement hash-bucketed gradual
rollout over `TrafficPercentage`, so its public surface offers to canary a
percentage of crews onto `UseSequentialCrewOrchestrator`. Nothing called it. Its
only consumer was its own test file.

**Removed** (breaking, packages): `Orkeon.Application.Abstractions.Data`
(`DatabaseQueryOptions`, `IDatabaseProviderFactory`, `IDatabaseSecurityPolicy`)
and `Orkeon.Infrastructure.Data` (`DatabaseProviderFactory`,
`DefaultDatabaseSecurityPolicy`) — recorded in the public-API files but not
here. The migration is a namespace change, not a rewrite: the surviving types
carry the same names under `Orkeon.Tools.Abstractions.Data` and
`Orkeon.Tools.Data.Relational`, so a consumer sees "type or namespace not found"
and needs one `using` changed. `docs/architecture/security.md` names the
namespace now instead of the bare interface.

**`Orkeon.Infrastructure` no longer drags in two database drivers it does not
use.** `Microsoft.Data.SqlClient` (with its `Azure.Identity` /
`Microsoft.Identity.Client` chain) and `MySqlConnector` were referenced for the
removed `DatabaseProviderFactory` alone. `Orkeon.Tools.Data` references them and
is where they belong; every consumer of the Infrastructure package was carrying
their restore weight and CVE surface for code that no longer exists. `Npgsql`
stays — `Checkpointing/PostgresStateStore` uses it.

**A promoted team could not write the deliverable it was built to produce.** The
read mount is derived first, so a deliverable landing under `/workspace` met a
read-only entry and was skipped on the name alone: the launcher spelled
`/workspace:ro`, the deliverable resolver logged a warning, and the run reported
that it had finished. Studio's sibling derivation deduped the other way and left
a read-only `/workspace` beside a read-write one — two chips for one root, the
one promising a write being the one silently dropped. Both hold the same rule
now: one root, one mount, and a write requirement wins over a read one.

**A deliverable folder named with a `:` or a `;` produced a launcher that died at
every start.** Those are the mount grammar's own separators — the launcher quotes
the physical segment and spells the virtual one bare — so `--mount
"…/rapports:2026":/rapports:2026:rw` reached `FileSystemMount.Parse` as four
parts, after `ForgePromoter` had already created the folder, so the team looked
complete. `ForgeBlueprint.Validate` refuses the root at submit time, where a
repair turn can rename the folder, and the derivation refuses it again.

**`FORGE.md` recommended the command the docs warn about.** The card said "the
folder is ordinary: `orkeon run <dir>/crew` launches it too" — which is true, and
launches it *without* the `--mount` arguments the launchers supply, so a team
with deliverables writes nothing and reports success. Both getting-started pages
and the CLI reference carry that caveat; the card is what the colleague receiving
the folder reads, and it was the last surface still giving the bare command.

**The launcher followed a symlink to the wrong folder.** `dirname "$0"` on a
symlink gives the *link's* directory, so symlinking "run this team" onto `PATH` —
normal for a folder the card calls ordinary — made the launcher mount `~/bin/output`
and die naming folders the user never created. It resolves the link chain first,
with plain `readlink` rather than GNU's `-f`, and the `cd` now carries `|| exit 1`.

**An `appsettings.json` mount was silently dropped on every single run.** The
runner writes its own mounts into `Orkeon:FileSystem:Mounts:0`, `:1`, … from an
in-memory source added last — which wins on an identical key. So index 0 did not
add a mount, it replaced the operator's first one; and `cliMounts` is never empty
in a real run, since the crew mount is inserted at index 0 before this code sees
it. Every tool touching that mount then failed "no mount found", with nothing
anywhere saying a mount had been dropped, and `orkeon-host` lost one per hosted
crew directory. The overwrite had already been found and fixed for the sibling
key `PathSecurity:AdditionalAllowedDirectories` — in the same pass that left it
standing here, and then copied its shape into the brand-new `InternalMounts` key.
All three append now, and the regression test asserts it on the merged registry
rather than on the mount strings, which is what the existing suites looked at.

**`docker build .` did not build.** `src/Directory.Build.props` imports the file
above it with an unconditional `<Import>`, so with the root `Directory.Build.props`
absent the expression evaluates to `""` and MSBuild refuses it (MSB4020) —
`restore` tolerates the empty import, `publish` does not, which is why the layer
that fails is not the layer that looks wrong. Replacing the hand-written project
list with `COPY src/` did not fix the Dockerfile, and nothing in CI builds this
file, so the claimed fix was never executed once. It is copied now, and the image
builds.

**`crew.process` was validated where nothing could recover from it.** Unknown
values are refused rather than silently becoming Sequential — but
`ForgeBlueprint.Validate` never checked the field, so `blueprint_submit` accepted
`process: pipeline` and answered "Blueprint submitted", and the throw landed in
`ForgeBlueprintCompiler.Compile`, called un-guarded from the validate stage, the
render stage and `ValidateEditedBlueprint`. None of them turns it into a
validation error, so it never reached the two-attempt repair loop built for
exactly this: the session died at the CLI boundary with the interview and
blueprint turns already paid for, and `forge resume` reloaded the same artifact
and died at the same point. The check now runs at submit time, where the model
can act on it.

**An internal mount was a boundary in the virtual namespace only.** The commit above
refuses the name `/llm-logs`; it refused nothing to the bytes. With the log
directory nested inside an agent-facing mount — the ordinary arrangement, since
`--llm-log ./logs` needs no `--allow-external-mounts` precisely because it stays
under the working directory, and the working directory is what gets mounted for
the agents — `/workspace/logs/llm-exchanges-….jsonl` returned the very file that
`/llm-logs/llm-exchanges-….jsonl` was refused for, and `ToVirtualPath` handed
that address out. Both directions enforce physical containment now. Its own test
suite mounted the two directories as siblings.

**`/sandbox` was mounted in every host and usable in none.** Resolving a virtual
path is two steps: the registry answers *where*, then `IPathValidator` answers
*whether*. Moving the sandbox mount into the registry fixed the first and left
the second denying it — the session directory lives under the temp directory
while the validator's workspace root defaults to the current one — so the first
call of every code execution kept failing, saying "Path is outside the allowed
workspace directory" instead of "No mount found". No flag rescued it:
`--allow-external-mounts` whitelists the CLI and internal mount lists, and the
sandbox root is in neither, because it is injected rather than configured. The
session root is registered as an allowed directory, and the test exercises the
real validator instead of stubbing it.

**The sandbox janitor deleted directories it had not created.** It swept every
subdirectory of `EphemeralRoot` older than the threshold, recursively, checking
neither the name nor whether the owning process was alive — and it now runs in
every process that builds a VFS rather than only in a started host. A directory's
mtime freezes once its direct children exist, so a daemon idle past the threshold
looked exactly like an orphan and a CLI invocation would delete its sandbox
mid-run; and since `EphemeralRoot` is a free-form string whose directory this
code creates, pointing it at an existing folder made the sweep a recursive delete
of user data. Both guards are in place. Runner flows also dispose their host now,
so the session directory goes at the end of the run rather than waiting for a
later sweep, and `/sandbox` joins the reserved virtual roots — a user `--mount`
claiming it was crashing a DI factory instead of printing the one-line refusal.

**A stalled embedding endpoint killed the crew and blamed the user.**
`TaskAgentSelector` rethrew every `OperationCanceledException`, but an HTTP
timeout inside the embedding backend surfaces as one too — and definitionally is
not the crew's token, since the adapter passes `CancellationToken.None` down. It
went straight past the degrade-to-round-robin path the class exists for, and the
terminal event reported a cancellation nobody requested. The guard is conditioned
on the caller's token.

**The scripting typings did not compile, in a new way.** `tools.d.ts` traded an
index signature a namespace cannot carry for a `namespace tools` beside a `const
tools`, which do not merge (TS2300/TS2395); `llm.d.ts` introduced a second
`LlmConfig` colliding with the one in `agent.d.ts` (TS2687 on all six members,
TS2717 on `model`) — a net-new break in a file that pass never opened. Both
passed the new typings test, because it runs esbuild: a transpiler strips types
and reports neither. `tools` is one interface with one `const` now, `LlmConfig`
is declared once, and the suite gained a check for the duplicate-declaration
shapes a transpiler structurally cannot see. The removed `agent.d.ts` copy also
documented a literal the runtime discards — `ExtractLlmConfig` returns null for
anything that is not a `JsLlmConfig`.

**The inbound process-type map still collapsed unlisted modes into Sequential**,
and the DTO enum stopped four modes short of the six the domain carries, so a
crew created through `CrewMapper` could not be Graph or Autonomous at all and
asking for one produced a Sequential crew that ran to completion. The enum
carries all six; an out-of-range value is an argument error. The test that
asserted the fallback asserted it by name.

**A command that is not installed put a disk path in front of the model.**
`ShellCommandTool` now hands `ProcessStartInfo` a resolved physical working
directory, and `process.Start()` sat outside the outbound rewrite — so any
allowlisted-but-missing binary returned "…with working directory '/tmp/…'" to
the LLM. The start is redacted like every other outbound string.

**Two containment copies survived the pass that claimed to unify them.** The
registry's own anti-traversal check — inside the very method the boundary suite
exercises — and both guards in Studio's `TeamCatalog` hardcoded `Ordinal` and
knew nothing of `AltDirectorySeparatorChar`. All five call sites route through
`PhysicalPathContainment` now, and the type's own doc says five rather than
three.

**The VFS exception table pointed at a file that was deleted in the same pass.**
`docs/architecture/vfs-compliance.md` and its French mirror still listed
`Scripting.Cli/CliFileSystemService.cs` as a ratified permanent exception —
doubly wrong, since that file was never in the analyzer's allowlist to begin
with; it carried an inline suppression. The neighbouring bootstrap row was
updated for `SandboxSession` in the same edit, so the row was read and left.

## [1.0.0-rc.2] - 2026-08-25

### Added — Remediation v3: what a run costs, and adoption that is no longer a one-way door

The owner's third design pass (RC2-FEAT-06) lands two engine-backed features and
a conformity sweep of the wizard.

**Usage metrics on the wire (W-08).** The cache dimension joins the token
telemetry end to end: `TokenUsage` carries the prompt-cache hit/miss pair (a
*partition* of the prompt tokens, never an addition) with a computed hit ratio;
every orchestration strategy stamps tokens+cache on its per-task snapshots; the
`CostUsageEvent` sink channel carries the same pair. `orkeon run`'s
`run.finished` now says what the run cost (`durationMs`, prompt/completion
split, cache pair), and the forge trial's `run.finished` and `verdict.ready`
carry the trial's own figures — distinct from the session-cumulative
`cost.updated`. Studio shows them as one chip recipe («12 840 tokens» ·
«cache 62 % · 7 980 tokens» · «59 s») on the verdict card, the launcher's finish
line and the history entries (tolerant schema). Not measured = no chip, never a
zero.

**Modify, re-try, re-adopt (W-09).** `forge resume` of a promoted session (or an
abandoned one that reached a verdict) reopens it at the arbitration — the stored
verdict re-announced first; a new `retry` decision re-runs the trial as-is (zero
compose tokens, one budget iteration, refused recoverably on an exhausted
budget); `forge promote` to the session's own `promotedTo` updates the team
folder in place (generated files regenerated, user files preserved, a dropped
schedule removed) — any other non-empty destination stays refused. In Studio,
« Modifier » on a team card reopens the wizard at Composer with the stepper
fully reachable and the adoption fields seeded from the sidecar; re-adoption is
pinned to the original folder; the saved card offers « Modifier l'équipe » and
« Refaire un essai ». The blueprint's `crew.name` must now be a short display
name (≤ 60 chars, repairable error) — it becomes the team's folder name.

**Edit at the dry pause (W-10).** `forge resume <slug> --edit` amends the
blueprint of a session paused before its trial: the amended JSON travels as the
channel's first inbound line, is validated in full (parse, compile, tool
catalogue), re-announced `blueprint.ready` on the **current** iteration — no
charge: that iteration's trial has not run yet — then re-rendered
deterministically; with `--dry` the session pauses again at the same boundary,
and an invalid edit leaves it exactly where it was (recoverable
`FORGE-BLUEPRINT-INVALID`). This wires Studio's « Modifier » on the Composer
step's agent cards, which was greyed out at the pause: the agent editor now
applies through a `resume --edit --dry` child run and the Composer repaints with
the amended team. At the arbitration, the `edit` decision remains the path.

Conformity (mock v3 volets): the stepper pills centre number and label; the
verdict buttons live in the verdict card (accept primary); one shared chip
recipe (`ToolChip`/`ChipAction`) across every tool, mount and metric chip; card
headers align title and mono meta; the « Définition générée » card shows the
rendered YAML itself with a Copy action; the Adopt step's model setting is a
profile card with unfoldable radio rows (no ComboBox); the Composer folder row
shows the mounts the agents imply (deliverable roots + the sandbox read mount).
Also fixed: a cold resume of the Composer pause left « Essayer l'équipe » dead
(the hydrator now restores the session identity from `session.json`).

**True half-circle pills (W-11).** WPF, unlike CSS, does not clamp
`CornerRadius` to half the element's height — the `999` reflex deformed every
chip's ends into ogives. Every pill now carries a fixed height with a radius of
exactly half of it: `ToolChip` 22/11, new `MetricChip` 20/10 (tokens, cache,
wall time), `BadgeBase` 20/10 (plus `BadgeNeutral`/`BadgeSky` variants), and no
chip border hard-codes a radius in a view any more. Action buttons are not
pills: `ChipAction` drops from 12.5 to the 10 cap. A conformity test sweeps the
XAML (no oversized radius anywhere) and pins radius == height/2 on the pill
styles.

### Added — Remediation v2: team folders end to end, and the blueprint edited by hand

The owner's second design pass (RC2-FEAT-05) closes the gap between the v3 mock
and the app around one idea: **a team's folders are part of the team**. The
adopted team's sidecar now records its mounts; the "Mes équipes" cards show them
as chips with a three-tone badge (scheduled / to try / on demand), the agent
count, the last run from the launch history and the model setting; « Changer les
dossiers » opens the team-mounts modal, and every folder choice goes through the
shared « Autoriser un dossier » picker (path + browse, one-level tree, rights in
two radio rows, expert mount-string preview). Studio lays those mounts on each
launch as `--mount` arguments — the chips and the command cannot disagree.

The forge protocol's reserved `edit` arbitration is implemented: interactive
mode now arbitrates every verdict (accepting a conforming one costs one click),
and `decision.made {edit}` followed by `blueprint.edited {blueprint}` re-renders
deterministically — zero LLM tokens — then re-earns its verdict through the
unchanged validate/test/diagnose path. The wizard's Composer step shows one card
per blueprint agent (its own goal and tools) with « Modifier » opening the agent
editor (name ↔ role, one-sentence goal, togglable capability chips), plus
« Ajouter un agent » and the « Dossiers de cette équipe » block written into the
sidecar at adoption. A novice help rail explains each step in plain words.

Also: « Validation à blanc d'abord » on the Exécuter screen (a failed dry pass
stops the launch), the import report's folders row, the expert TRIAL COMMAND
card, « Copier le rapport » in the Diagnostic header with its « Copié ! »
feedback, the plain-words novice title on the checks card, the Limites cards'
descriptions under their titles, the export action on team cards (never copies
`appsettings.json`), and the removal of the settings validation card (the status
line reports instead).

### Fixed — Kimi's server-mandated temperature self-heals instead of killing the run

Moonshot rejects some models' requests with `400 invalid temperature: only 1 is
allowed for this model` — and which models mandate it is decided server-side.
`KimiLlmProvider` now reads the constraint from the API's own rejection and
re-sends the request **once** with the mandated value, logging a structured
warning (never a silent substitution). The seam is a new overridable on
`OpenAICompatibleProviderBase` (`TryAdaptRejectedPayload` — one adaptive retry
on a 4xx, non-streaming paths only), available to any provider with a
server-stated constraint. Before this, a Studio wizard run on a Kimi profile
died at the brief stage after three identical rejections.

### Changed — The WPF screens remediated against the v3 mock (audit T-01…T-13, screens 01–22)

A full-screen audit against the design mock (the mock is the source of truth)
drove a six-lot remediation of `orkeon-studio`:

- **Foundations** — the logo's rings rotate as one group (they were skewed by
  double rotation origins); MODE and EN/FR are real segmented controls; implicit
  styles for TextBox/ComboBox/CheckBox/RadioButton/ScrollBar erase the native
  Windows chrome; every raw `CornerRadius="999"` outside badges is bounded to
  height/2; the splash mascot PNG is re-flattened so its corners land exactly on
  the plate colour. The window holds a **1024×768 minimum**.
- **Run** — rebuilt as the mock's vertical cards: a sidecar-backed team card, a
  plain-language progress card (state title, tone badge, "Ouvrir le résultat" on
  the first writable mount) and a technical journal folded by default in novice;
  COMMANDE and the option rows are expert-only; a localized banner replaces the
  raw locator message when the CLI is missing.
- **Language** — every user-visible French string says « équipe » (pinned by a
  resx test); mode-dependent subtitles carry the mock's wording; validator
  findings and doctor checks show a per-code plain-language overlay first, the
  raw CLI-grade line staying as expert detail; the Limits cards are titled in
  plain language.
- **Screens** — History becomes a card list with per-run duration
  (`LaunchHistoryEntry.DurationSeconds`, tolerant migration) and per-card
  Replay/Open-result; Test is two columns with a full-height console and a
  sample-inputs field; Import gains a drop zone, a single Browse, a recognition
  report and an expert "Test it first" hop; the mounts form no longer opens in
  an error state and novice gets one card per folder plus an "Allow a folder"
  picker; Diagnostic opens on a verdict card fed by a silent first doctor run;
  the Create gate is a radio list.
- **Startup & secrets** — the window opens at the mock's 1024×768 baseline
  (still resizable up); the Réglages model tab gains an **"API keys" card**:
  one row per environment variable the profiles resolve (status chip, paste
  field, "Mémoriser la clé"), storing through the same `IApiKeyStore` as the
  profile editor — the value lands in a user environment variable and the
  field empties once stored; no key ever enters a file.
- **Polish** — novice Settings auto-saves on every edit (Save/Validate stay
  expert; the validation card is expert-only, keeping the copyable report);
  profile cards carry "Utilisé par" team chips; Limits cards say when the
  engine's defaults apply; the raw-JSON viewer is bounded; the teams counter
  is a quiet right-aligned figure; History/Test/Run have empty-state phrases;
  team cards gain an "Ouvrir le dossier" explorer action.

### Added — Every provider one click away, the API key novice-proof, and a screenshot campaign

The model-profile editor (design v3, "volets" rev. 2) now carries the **full provider
catalogue**: the two local runtimes (Ollama, Docker Model Runner — free, keyless), one
card per cloud the framework ships a provider for (OpenAI, Anthropic, DeepSeek, Mistral,
Gemini, Groq, Together AI, Qwen, Kimi, HuggingFace, Z.AI — endpoint, default model and
key variable pre-filled from the drift-pinned runtime constants; Azure OpenAI stays a
"Compatible OpenAI" entry by design), the OpenAI-compatible catch-all and the echo
fallback, grouped as the mock groups them. A novice clicks a card and pastes the API
key **in the editor**: "Mémoriser la clé" stores it in a user environment variable
(the vendor's conventional name — `DEEPSEEK_API_KEY`, `ANTHROPIC_API_KEY`, …), the
status chip says whether one is in place, the vendor's key console is named, and the
connection test refuses to probe into a guaranteed 401 when the key is missing. The
profile store only ever carries the **name** of the variable (`ModelProfile.KeyEnvName`
— the round-trip test forbids the `ApiKey` substring in the file as a tripwire);
launches resolve it and lay the value over the child process as `ORKEON_Llm__ApiKey`
(Run, Test, history replays, and the wizard's assistant alike).

`orkeon-studio --capture-screens <dir>` walks every screen in both modes (plus the
profile-editor and About overlays) and writes one PNG per stop — the
fidelity-remediation reference collection against the design mock, one command on a
Windows machine.

The Diagnostic screen gains **"Copier le rapport"**: WPF text blocks are not
selectable, so the whole `orkeon doctor` result (verdict, every check verbatim, parse
error if any) is now one click away from the clipboard.

The Settings screen's validation card gains the same copy affordance as the
diagnostic ("Copier le rapport"): summary line plus every finding with its severity,
verbatim.

`OrkeonBinaryLocator` learns the **development-checkout layout**: when Studio runs
from its own `bin/` inside a clone (detected by walking up to `Orkeon.sln`), it probes
`src/scripting/Orkeon.Scripting.Cli/bin/<Configuration>/<tfm>/` — same configuration
first, the sibling second — so F5-from-the-IDE finds the CLI the repo just built
instead of reporting it missing. The not-found message now names the dev gesture
(`dotnet build src/scripting/Orkeon.Scripting.Cli`) — and the whole lookup is now
operator-steerable, in order: the **`--cli-dir <dir>`** argument (WPF app), **next to
the executable**, the **`ORKEON_CLI_DIR`** environment variable, `PATH`, then the
development checkout. The not-found message lists that exact order.

### Changed — Full documentation audit against the implementation (DOC-04)

A five-domain adversarial audit (getting-started/README, architecture, RAG/RaggableTree/
EventHub/ADRs, reference, orchestration/guides) verified every factual claim in the docs
against the code and fixed ~140 confirmed discrepancies, in both languages. The heaviest
classes: **phantom APIs** (`AddOrkeonRuntime`, `CodeSecurityAnalyzer`/`CodeSandbox`/
`SensitiveDataDetector`/`ComplianceChecker`/`RetryPolicy`-family types, the
`ValidateServerCertificate` opt-out, `manager_llm`/`autonomous_budget`/`state_timeout`/
`json_search`/`csv_search` YAML keys, the `raggableTree:` crew-YAML section, the
`Exp07CommandSurfaceTests` suite, `Orkeon.Examples.Runner`); **YAML samples teaching a
shape the loader silently loads as an empty crew** (a `crew:` root wrapper and
sequence-form `agents:`/`tasks:` — the schema is flat-rooted with id-keyed mappings, now
taught correctly in process-types/autonomous/blueprint); **stale capability matrices**
(response-format and vision are capability-driven across the 13 providers — not
"DeepSeek only"/"Anthropic+OpenAI only"; Anthropic thinking is `toggle`, `adaptive` is
its wire value); **wiring claims corrected to what composition roots actually do**
(the `AddOrkeonInfrastructure` overload capability table, MCP being effectively
library-only, the empty `InMemoryToolRegistry` stub vs `ServiceProviderToolRegistry`
in both tool-authoring guides, StrictTools lenient library default, EventHub telemetry/
metrics marked designed-not-built, the autonomous YAML budget being `Permissive` not
`Default`); plus the settings-resolution chain, the complete `orkeon run` flag table,
`yaml-schema.md` gaining the six shipped blocks it omitted (`llm:` full surface,
`links:`, task `tools:`/`deliverable:`/`llm_override:`), `[ToolContract]` added to the
new-tool guide, VFS diagnostic help-link anchors, and ADR-002/005 amendments recording
their drifted counts.

Four code fixes rode along: `RunArgumentsBuilder` now emits a **single** `--mount`/`-V`
flag with space-separated values (the CLI parser rejects a repeated option — multi-mount
and multi-variable Studio launches were broken); `AddOrkeonApplication(Action<…>)` no
longer silently drops `AgentSelectionStrategy` from the options copy; the three
`examples/runners` packables are re-aligned on `1.0.0-rc.2`; and the plugins CA2000
suppression justification now states the real ownership contract.

### Changed — The tool inventory rebuilt against the implementation, and gated (DOC-03)

`docs/tools/inventory.md` (and its French mirror) claimed 43 tool names out of the 79
built-in tool classes, listed two tools that do not exist (`bing_search`,
`google_search`), used registry names the code never had (`delegate_work` and
`ask_question` — the real names are `delegate_work_to_coworker` and
`ask_question_to_coworker`), and flatly denied XLSX support while `xlsx_reader` and
`xlsx_writer` ship in `AddOrkeonDataTools()`. The page is rebuilt as the reference
catalogue: every one of the 79 tools with its exact agent-visible name, class, owning
DI extension and secrets policy; an availability matrix per composition root
(`orkeon run` vs the REPL — they do not register the same suites); the complete
alphabetical YAML mapping; an "outside the catalogue" section (forge-internal tools,
`McpToolAdapter`, test doubles); and the counting rule stated so the number is true by
definition. Secondary surfaces follow: `autonomous.md` no longer implies `spawn_agent`
is wired (no shipped root registers it — now a documented limitation) and its YAML
example stops citing the nonexistent `json_search`/`csv_search`; `bootstrap.md`'s suite
comments match what each extension actually registers; the porting guide and the
READMEs mention Office (DOCX & XLSX) explicitly.

`scripts/check-doc-claims.py` gains the gate that makes the regression class
impossible: it extracts every agent-visible tool name from the code (the
`[ToolContract("…")]` positional or the `Name => "…"` literal of each counted file) and
fails the build if a shipped tool is missing from the inventory (either language), if
the inventory tables a name no tool bears, or if the summary total drifts.

### Changed — Orkeon Studio v3 "volets": the WPF app becomes a team-lifecycle product

The Claude Design v3 handoff is implemented end to end. The window's sidebar now follows the
life of a team — **Agent teams** (Create a team, My teams, Import), **Work** (Test, Run,
History), **Environment** (Settings, Diagnostic) — under a global **Novice/Expert** switch
persisted with the theme and language: Novice explains each step and shows contextual help,
Expert shows the machinery (command lines, raw JSON, technical journal, the Test screen).
The window opens on a startup screen (Kama, the mascot — click to skip) and carries an
About overlay; the guided tour is rewritten to the five v3 stops.

- **Settings, unified**: the former Start/Sections/Mounts/Raw screens fold into one
  "Réglages" entry with four inner tabs (AI model, authorized folders, expert-only
  limits & logs, expert-only raw file with its location and resolution chain). The model
  tab introduces **named model profiles** (`studio-model-profiles.json`, next to
  `studio-history.json`): reusable settings, a default election mirrored into the `Llm`
  section through the ordinary save cycle, per-profile `ORKEON_Llm__*` environment
  overrides, and the profile Studio's own assistant runs on. The API key never enters the
  store. The preset picker retires; the profile editor offers the same `orkeon init`
  catalogue with a live connection probe. The Diagnostic sidebar entry gains a warning dot.
- **The creation wizard replaces the Atelier UI** (breaking for the screen, not the
  engine): "Créer une équipe" walks Décrire ▸ Composer ▸ Essayer ▸ Adopter over the same
  `orkeon forge --events jsonl` child — the stepper projects the engine's milestones, the
  per-step "consigne + questions" blocks travel down `user.message`, arbitration buttons
  come from `decision.needed`'s own options, "Fix and retry" carries the trial consigne,
  and the wizard is gated until the assistant has a model profile (handed to the engine as
  environment overrides). `ForgeClient`, the session model and the protocol are unchanged.
- **My teams**: adoption promotes straight into `~/Orkeon/teams/<slug>` with the engine's
  real schedule grammar (on demand, `daily@HH:mm`, `hourly`) and writes a
  `studio-team.json` sidecar (name, need, profile, displayed schedule — never a key).
  The sidecar's profile rides every launch of the team — Run, Test, and history replays —
  as `ORKEON_Llm__*` environment overrides resolved against the profile store.
  Team folders are listed as cards (launch, duplicate, delete), next to the wizard
  sessions still underway, resumable where they stopped.
- **Import and Test**: "Importer" recognizes a shared team with the launcher's own
  detector, warns loudly when a definition carries a pasted secret, and copies into the
  teams root only on confirmation, under a never-overwriting slug. The expert "Tester"
  screen runs blank-run validations and real trials over a dedicated launcher with no
  history store. In Novice, the run screen hides the options machinery entirely.

The two Terminal.Gui apps (`orkeon-studio-config`, `orkeon-studio-run`) keep their current
surface. Nothing is deployed, so the old WPF screens are removed without shims; the resx
pair moves to 395 keys per language, still pinned by the drift and parity tests.

### Added — Studio staffs the `client://studio` seat: agent requests answered on screen

An agent's `send_request` to the watching client no longer dies of a 30-second timeout: the
Launch screen shows the request and a human answers it.

- **Protocol**: `hub.message` gains an `expectsReply: true` field, emitted **only** on an
  agent's `send`. The peer must not have to guess which correlated lines are questions — a
  topic relay can carry a `correlationId` too, and the answer to the peer's own `send` pairs
  by correlation as well. The field is additive; readers that ignore it lose nothing.
- **Studio Core**: `RunProgressModel` folds marked sends into a `PendingAgentRequest` queue
  (dedupe by correlation id, cleared at `run.finished`, `ReplyAccepted()` mirroring
  `AnswerAccepted()`), alongside the untouched `HubMessages` journal.
- **Studio WPF**: a request panel — asking agent's hub address, raw JSON payload, reply box —
  in the Launch screen's progress card. The typed reply goes down stdin as the
  `{"kind":"reply",…}` line the bridge requires; text that parses as JSON travels as that
  JSON (an agent may await a shape), anything else as a plain JSON string. A refused write
  keeps the request on screen. Hub posts are now listed in a compact panel instead of being
  parsed and shown nowhere. Silence past the agent's own timeout remains a refusal — the
  screen just gives the human a chance to speak before it.

### Added — Real Discord slash commands: `/status` and `/stop`, registered and gated

The host's two commands stop being text parsing and become what Discord users expect:
registered slash commands, autocompleted by the client, answered **ephemerally** — a status
poke or a refusal is the invoker's business, not one more line in everyone's thread.

- **Registration at connect**: globally when `Discord:GuildIds` is empty (zero configuration,
  Discord caches global commands up to ~1 h), or per named guild for immediate availability
  (the dev loop). A registration hiccup logs a warning and leaves the message channel alive —
  it never kills the daemon. An unparseable guild id refuses the start (exit 78).
- **The text parsing is removed** (nothing is deployed, no dual path): the platform's client
  intercepts the slash, and a literal `/stop` arriving as plain message content is a prompt
  like any other.
- **Security fix along the way**: the Stop **button** used to bypass the allow list entirely —
  `component.User` was never read, so anyone who could see the thread could kill a run while
  typing `/stop` was gated. Button and slash commands now converge on one
  `CommandInvocation` path checked against `AllowedUserIds`; an unauthorized click gets an
  ephemeral refusal instead of a silent stop.
- The privileged `MessageContent` intent stays: runs are started by plain thread messages,
  which slash commands do not replace.

### Added — The Atelier: `orkeon forge`, from a need in plain words to a deployable crew (FORGE-01→08)

The missing step between "I have a problem" and a running agent team. `orkeon forge
"summarize my supplier's new offers every morning"` opens a short interview, captures a
structured brief — goal, inputs, **acceptance criteria**, a sample input — plans a team,
renders it as ordinary crew files, validates it, **tries it in a sandbox on that sample**,
and judges the result against the criteria the user stated. Not conforming? The diagnosis
feeds a refine loop, bounded by a hard three-dimension budget (iterations, tokens, wall
time). `orkeon forge promote <slug> --to <dir>` then ships the crew as an ordinary folder.

The spine is deterministic — a `StateMachine<ForgeState, ForgeTrigger>` with 11 states,
checkpointed after every step — and only schema-validated `brief_submit` /
`blueprint_submit` submissions advance it: the conversation is carried by an embedded crew
(the scripting DSL running under Jint, `ctx.llm.act()` reached natively), but it never
steers the cycle. Sessions live under `.orkeon/forge/<slug>/` — resumable
(`forge resume`), diffable between attempts, auditable — and every run emits a versioned
JSONL event protocol (`--events jsonl`), golden-pinned on both sides of the wire.

- **Two formats, one generation.** The assistant produces a single schema-constrained
  blueprint; two deterministic renderers derive from it — the per-entity YAML layout
  (default) or an editable `crew.ork.ts` (`--format script`, needs esbuild; absent, a new
  session falls back to YAML with `FORGE-ESBUILD-MISSING`). Both converge on the same
  `CrewDefinitionValidator`, and the sandboxed try loads the crew **from the rendered
  files** — what was written is what runs.
- **The sandbox restricts by removing tools from the catalogue**, not by hoping the model
  abstains: `shell_command` and `code_interpreter` are gone from the list that feeds *both*
  the blueprint prompt and the validation, and writes are confined to the session's
  `/output` mount, snapshotted per run.
- **The verdict is recomputed, never trusted**: score ≥ 0.7 *and* no blocking finding, with
  a missed `must` criterion blocking regardless of score. With no judge available, the
  verdict announces itself as `deterministic` and leans on mechanical checks — it never
  invents a passing score. Accepting a non-conforming result on sight stays legitimate.
- **Promotion is honest about its limits**: the folder carries `crew/`, `run.sh`/`run.cmd`
  composed against the CLI's own `orkeon run` grammar with the sample inputs pre-filled,
  and `FORGE.md` — the crew's identity card, written in the interview's language. With
  `--schedule daily@HH:mm|hourly`, the Windows task XML, systemd timer and cron line are
  generated and the install command is **displayed, never executed**: Orkeon has no
  scheduler, and pretending otherwise would promise supervision it cannot give.

**In Orkeon Studio**, the same engine drives a new **Solve** screen (WPF): a conversation on
the left, one card at a time on the right — success criteria, the proposal in plain words,
the live try, the ✔/✘ checklist quoting those criteria verbatim, then "what now?". Studio
spawns `orkeon forge --events jsonl` as a child process and answers over stdin; it never
touches an LLM itself, and a capability absent from the event stream exists on no screen.
`Orkeon.Studio.Core` gains a `Forge/` client (tolerant parser pinned against the CLI's
golden lines, session projection, catalogue, resume hydrator) without a single new
dependency. `RunSession`/`RunLaunchRequest`/`TargetSelectionModel`/`LaunchOptionsModel` were
promoted into `Orkeon.Studio.Core.Launch` along the way, so the launch lifecycle now exists
once instead of twice.

Docs: [Forge a team from a need](docs/getting-started/forge-a-team-from-a-need.md) (EN/FR),
the `orkeon forge` section of the CLI reference, and `examples/forge/promote-demo/` — a
bundled ready session whose `list`/`promote` half runs with no LLM at all.

### Added — Watch a run, talk to it, host it: the event bus, the hub pipeline, the service host (BUS, HUB, GATE)

Three chantiers that share one goal: a crew you can see, answer and reach from outside the
process it runs in.

**`orkeon run --events jsonl`** speaks a versioned protocol instead of printing for a person —
one JSON document per line out, one per line back. Task completions in **all six orchestration
modes** — terminal events included: a cancelled or failed run still says so, in every mode —
cost with model and provider (no invented price: the framework has no price table), generation
deltas under `--stream`, tool calls, delegations, and runtime agent spawns when a `spawn_agent`
tool is attached. Tool events come from a single decorator applied where tools enter the
process — the DI registrations, and through the `IToolDecorator` port the per-agent delegation
pair — so the agent loops and the scripting facade are covered at once, `.ork.ts` targets
included. Contract: [The run event bus](docs/architecture/run-event-bus.md),
plus a ~90-line dependency-free reader in `examples/run-events/` with a recorded stream to try
offline.

- **Silence is not consent.** Without the protocol, a task declared `humanInput: true` was
  auto-approved behind the user's back. Asking for the stream replaces that provider: the
  question goes out and the run waits. No answer — closed channel, cancelled run — is a
  **refusal**, never an approval.
- **Studio's Launch screen stopped being a terminal.** It shows finished tasks, cost and the
  run's question on screen; the raw log is demoted, not removed. A silent run says "nothing
  reported yet" rather than implying progress, and a question with no correlation id is not
  shown as pending, because answering needs an address.

**The EventHub's middleware pipeline** ships complete: logging, telemetry, ACL, idempotency and
validation, on both the publish and the receive path. The `links:` YAML grammar lets a crew
declare who it may talk to, and `client://{name}` lets an external process be named — without
it, a watching peer would escape the ACL by simply not being modelled. Idempotency guards
point-to-point delivery only: deduplicating a topic message by identifier would starve every
subscriber but the first. Its memory does not survive the process, which is stated rather than
implied.

**`orkeon-host`** hosts crews as a daemon — systemd unit, Windows service or container, same
binary, and runnable in a terminal because a daemon you cannot run in the foreground is one you
cannot debug. One dependency-injection scope per run, which is what keeps one conversation's
memory from reaching another's. A Discord channel gives the first place people can reach a crew
from: allow-list authorization checked **before** routing, thread-is-run mapping, an immediate
acknowledgement carrying a Stop button, throttled progress, `/status` and `/stop`. An empty
allow list denies everyone and refuses to start.

It hosts crews; it does not schedule them. rc.2 ships no scheduler, and neither the docs nor the
systemd unit implies otherwise. Docs:
[The service host and the chat gateway](docs/architecture/service-host.md) (EN/FR),
`examples/service-host/`, and `deploy/` for the unit, the SCM script and the Dockerfile.

### Changed — EventHub API surface, and the crew configuration

`CrewLink` and `CrewLinkDirection` live in `Orkeon.Domain.EventHub`: a link is what a crew
*declares*, not a transport detail — and it names its target by the crew's **`name:`**, the only
identity a YAML author has (`ICrewLinkRegistry.Register` records the id ↔ name mapping for every
crew, links or not). `CrewConfiguration` gained a nullable `Links` (never-declared and
declared-empty are different answers to the ACL). `MailboxAddress` gained the `client://`
scheme. `Message.NoDeclaredSchemaId` is `"_none"` — a sentinel that cannot collide with a real
schema id. `CrewOutput` gained `Succeeded`, because `KickoffAsync` never throws and a host has
to tell an answer from an apology. `IMemoryService` gained `ReleaseMemorySystem`, the
daemon-side counterpart of loading a crew per message. The forge stream shares the run envelope
and speaks protocol **v2** (was v1); its reserved-name set grew from four to eight. Several
constructors gained an optional trailing parameter — the orchestration strategies
(`ICrewExecutionHook`), `InMemoryEventHub` (middlewares), `CrewFactory` (the link registry),
`AgentDelegationToolsProvider` (the tool decorator) and `RunnerHost.Build` (a builder hook).
Source-compatible; recompile.

### Fixed — Two harness defects that were reporting success

`TraceExplorerService` listened to every process-wide `Orkeon` activity source, so a test
asserting "no traces" was really asserting that no other test emitted one — true by luck, and
less true as the suite grew. Trace capture is now scopeable via `MonitoringOptions.TraceSourcePrefix`.

The E2E CLI tests rebuilt the CLI on every invocation, and two classes doing that concurrently
made MSBuild write its diagnostics onto the stream under assertion. The CLI is now built once
per assembly and run with `--no-build`.

## [1.0.0-rc.1] - 2026-08-18

Orkeon's first release candidate — the version that goes to NuGet.org. Everything
accumulated since `0.9.2-beta` ships here: the RAG subsystem with its five profiles and
the corrective CRAG graph, declared LLM capabilities across all **13 providers**
(Google Gemini joining as the 13th), the dual-era MCP client and server, A2A task
persistence, Orkeon Studio with full localization, the Windows/Debian/macOS install
channels, the TypeScript CLI command layer, the complete API reference site — and a
mechanically frozen public API surface (29 235 declared APIs, `[Experimental]` markers
on the four unstable areas) with the versioning policy to match. Breaking changes below
are called out in their own entries (RAG namespace extraction, API-shape conformance,
`Orkeon.Cli.Commands.Scripting` rename).

### Fixed — `orkeon llm probe|models` now know Gemini (PUB-15 leftover)

`LlmProviderFactory` accepted `gemini`/`google` since PUB-15, but the CLI's
`LlmCatalogClient` had no default base URL for it and did not treat it as
OpenAI-catalog-compatible — `orkeon llm models -p gemini` failed without an
explicit `-u`, and the probe help text stopped at 12 providers. Both wired;
surfaced by the DOC-02 review of the new CLI reference page.

### Changed — Documentation trued up against the code, end to end (DOC-02)

A three-pass audit (docs/ tree, root/examples/OSS surface, facts vs code)
followed by full remediation. Highlights: the quality-gate page no longer
claims a blocking Sonar CI gate that does not exist; the fictional
`autonomousBudget` YAML block is marked not-implemented; two lifted
limitations rewritten (uniform streaming, runtime plugins); every count
trued (79 tool classes, 44 domain events, 13 providers, 33+33 projects);
17 dead maintainer-side references and 4 failing copy-paste commands fixed;
~350 French fragments in EN docs translated; `docs/arkeon/` retired;
`CLAUDE.md` no longer rendered on the docs site; `docs/fr/toc.yml` created
(51 FR pages were orphans) and the ADR register joined the site nav.
New pages (EN + FR): `reference/cli.md`, `reference/configuration.md`,
`architecture/mcp.md`, `architecture/studio.md`. New guards in CI:
`scripts/check-doc-claims.py` (counts checked against the code), a
category-README completeness check, and an informational EN/FR drift
report. New community files: SUPPORT (EN/FR), NOTICE, CODEOWNERS,
dependabot, CodeQL, a documentation issue form.

### Fixed — ExecutionPlanParser no longer throws on non-string JSON values

`ExecutionPlanParser` parses untrusted LLM planning output, yet `"task": 42`,
`"instructions": 42`, `"agent": 42` or a number/null inside `"dependencies"`
escaped the `JsonException` net as an `InvalidOperationException` from
`JsonElement.GetString()` and took the whole planning pass down. Every
string-position read is now guarded by a `ValueKind` check: malformed entries
degrade the same way unknown ids always have (entry skipped, dependency
dropped, agent unassigned) instead of throwing. Surfaced by the SONAR-14
coverage pass on the parser.

### Added — Studio i18n: the whole below-the-view layer follows the language switch (STUDIO-11 tranche 2)

Completes the sweep opened by PUB-19: the `IStudioStrings` registry grows from 4
to 89 keys covering every string Core and the WPF ViewModels fabricate — the
`orkeon init` preset catalogue (labels, guidance, plan errors, via
`LlmPresets.CatalogFor`), the settings resolution chain
(`SettingsLocations.ResolutionChainFor`), the mount-rights labels
(`MountRightsTokens.ChoicesFor`/`GetLabel(rights, strings)`), the directory-run
notice, the `--mount` override semantics, and all ViewModel statuses, summaries,
dialog titles and filters. Every long-lived ViewModel takes the port (optional,
English default — TUIs unchanged) and re-emits its bindings on `CultureChanged`;
transient rows (mounts, overrides, effective-mount table) are refreshed by their
owners. 89 keys mirrored EN/FR in the WPF resx; a new drift test pins
resx-EN ≡ `EnglishStudioStrings` so the two English surfaces cannot diverge.
Deliberately untranslated (CLI-contract policy, like `VALIDATION OK/FAILED`):
`orkeon doctor` check names/details, validator message bodies, target-detector
remediations, LLM probe results, and exit-code descriptions.

### Added — Studio localization port: strings below the view layer follow the language switch (PUB-19 tranche 1)

Foundation for STUDIO-11 ("switching to French leaves some strings in English"):
`Orkeon.Studio.Core` gains a localization port (`IStudioStrings`, key registry,
English defaults) consumed by the shared formatters — `ValidationMessageFormatter`
and `LaunchOutcomeFormatter` first — through additive overloads (the TUIs keep the
English default, no regression). The WPF front bridges the port onto its
resx-backed `I18n` (`I18nStudioStrings`, hot language switch relayed via
`CultureChanged`), with the new keys mirrored EN/FR under the existing resx-parity
test. CLI verdict words (`VALIDATION OK`/`FAILED`) deliberately stay untranslated.
The ViewModel sweep continues on this pattern (tracked in STUDIO-11).

### Added — Test pyramid rebalanced: offline E2E per orchestration mode, nightly integration, no swallowed SIGSEGV (PUB-17)

- **E2E grows from 17 to 43 executed facts, all offline**: a new suite drives the
  full DI stack (Application + Infrastructure) through
  `ICrewOrchestrationService.KickoffAsync` for **each of the six orchestration
  modes** (sequential, hierarchical, parallel, consensual, graph, autonomous) with
  a scripted LLM — per mode: the kickoff completes, produces exactly one output per
  declared task, and demonstrably drives the LLM (no silent no-op path). Two
  `Category=Slow` facts spawn the real `orkeon` CLI from source and `--validate` a
  single-YAML example and a multi-file crew directory end-to-end.
- **`integration.yml`**: the Integration/Slow suites (Testcontainers databases) now
  run nightly (02:17 UTC, also dispatchable). A red run opens or comments a
  tracking issue — failures are visible, not buried in a log.
- **The blanket `continue-on-error` on Embeddings.Local is gone** (ci.yml and
  publish.yml): the step now inspects the results — a genuine test failure fails
  the build; only the known ONNX teardown crash (exit 139 **after** a clean
  "Passed!" summary) is tolerated, explicitly and with a warning annotation.

### Changed — Global zero-warning ratchet: full analyzer set on, CI builds -warnaserror (PUB-18)

The warning-debt story reaches its terminal state. The audit found the "frozen
~2 283 warnings" note in the build props was stale — the 2026-06-15 zero-warning
campaign had already resorbed the debt; a fresh full-analysis inventory
(`AnalysisMode=All`, `AnalysisLevel=latest-all`) surfaced only **four stragglers**
solution-wide, all fixed (unused TUI palette field, `DefaultDllImportSearchPaths`
on the libc `kill` P/Invoke, two per-call `JsonSerializerOptions` allocations).
The complete analyzer rule set is now **enabled permanently** in the root build
props, and CI compiles with **`-warnaserror`** — any new compiler, analyzer, or
NuGet-audit warning fails the build (audit advisories breaking CI is deliberate;
see the SSH.NET precedent). The `.editorconfig` ledger remains the record of the
deliberate per-scope arbitrations.

### Changed — DI default stand-ins fully documented; the stub planner now warns (PUB-23)

The deliberately-minimal DI defaults follow the house rule — never a silent drop —
and the last gap is closed: `AgentPlannerService` (the fixed 4-step stub planner,
the most misleading stand-in since it emits a plausible "plan" every run) now
announces itself with a **one-time Warning naming the replacement gesture**, like
the Infrastructure stubs already did (previously Debug-only, invisible under
default logging). New page `docs/getting-started/default-behaviors.md` (EN + FR)
inventories every default — the six that warn, the six that are silent by design
and why — with the exact replacement snippet for each; linked from limitations and
the Autonomous orchestration guide. No functional behavior change.

### Added — Google Gemini provider: 13th LLM provider (PUB-15)

`GeminiLlmProvider` joins the family through Google's OpenAI-compatible endpoint
(`generativelanguage.googleapis.com/v1beta/openai`, Bearer auth with the Gemini API
key). Capabilities verified against the compatibility documentation (2026-08-18):
effort-only thinking (`reasoning_effort` mapping to Gemini's `thinking_level`),
vision via `image_url` data URIs; `response_format` is undocumented on the compat
surface and therefore stays **undeclared** — a JSON-format request triggers the
structured capability warning instead of a silent drop. Factory auto-detection by
host (`generativelanguage.googleapis.com`) and model prefix (`gemini-*`); default
model `gemini-3.7-flash`; `appsettings.gemini.local.json.example` template added;
provider docs, comparison matrices and counts updated EN/FR. Vertex AI and AWS
Bedrock remain out of scope (OAuth/SigV4 SDK stacks conflict with the simple-HTTP
provider principle — recorded in the PUB-15 fiche).

### Added — A2A task persistence lifts the 501; conformance matrix published (PUB-08)

- **`GET /a2a/tasks/{id}` is real now** — opt-in: register a checkpointing state store
  (`AddOrkeonCheckpointing` / SQLite / Postgres) plus the new
  `AddOrkeonA2ATaskPersistence()`, and the A2A server records every task lifecycle
  transition (`IA2ATaskStore` port over the existing `IStateStore`, best-effort — a
  store outage never fails the task exchange). The endpoint answers `200` with the
  recorded state and `404` for unknown ids; `DELETE /a2a/tasks/{id}` now records the
  cancellation and answers `404` for unknown ids instead of fabricating an
  acknowledgement (cancellation stays advisory). Without the opt-in, the explicit
  `501` remains — never invented state.
- **A2A conformance matrix** (`docs/reference/a2a-conformance.md`, EN + FR): the
  honest inventory of the 0.x-era surface against the A2A v1.0 specification —
  operations, data model (5 vs 9 task states), bindings (own REST dialect; none of
  the three canonical bindings), security. Includes the PUB-08 certificate-revocation
  decision: short-lived certificates over CRL/OCSP for the private-CA mTLS model.
- 6 new persistence tests (adapter round-trip + end-to-end HTTP 200/404/501/cancel).

### Changed — MCP unpinned from 2024-11-05: dual-era client and server (PUB-07)

The MCP integration no longer hardcodes the first protocol revision. Both sides now
speak the **modern stateless lineage (`2026-07-28`)** and fall back to the legacy
initialize-handshake revisions, per the specification's backward-compatibility rules:

- **Client** (`McpClient.ConnectAsync`): probes with `server/discover`; a modern answer
  selects the newest mutually supported revision (renegotiating on
  `UnsupportedProtocolVersionError`, including mid-flight), anything else falls back to
  the legacy `initialize` handshake — which now really negotiates (`2025-11-25`,
  `2025-06-18`, `2024-11-05`) instead of pinning `2024-11-05`, and finally sends the
  required `notifications/initialized` (it never did). Modern requests carry
  per-request `_meta` (protocol version, client info/capabilities); `input_required`
  interim results (MRTR) are surfaced as explicit tool errors rather than partial data.
- **Server** (`McpServer`): dual-era on the same endpoint — implements the mandatory
  `server/discover`, validates the per-request declared version (`-32022`
  `UnsupportedProtocolVersionError` with the supported list), answers legacy
  `initialize` with real version negotiation, no longer replies to notifications,
  returns `tools/list` in deterministic order with the required
  `resultType`/`ttlMs`/`cacheScope` fields plus server identity in result `_meta`.
- **Wire**: JSON-RPC ids are no longer int-only (string ids from external clients now
  round-trip; stdio correlation is id-agnostic); the HTTP transport sends the
  Streamable HTTP headers (`MCP-Protocol-Version`, `Mcp-Method`, `Mcp-Name`) and
  unwraps SSE-framed response bodies.
- 13 new dual-era tests (104 MCP tests total). Remaining gaps are documented in
  `docs/reference/limitations.md`: no `subscriptions/listen`, no MRTR, no MCP OAuth,
  JSON-response mode only on HTTP, and `2025-03-26` excluded (mandatory batching).

### Added — Full API reference site and community templates (PUB-10, PUB-12)

- **docfx now covers every published library** (25 assemblies — 2 916 generated API
  pages, nested-namespace navigation) instead of core + Tools.Abstractions only.
  Metadata is generated from the compiled Release assemblies, so source-generated
  members are included. The never-finished navigation is real now (`toc.yml`,
  `docs/toc.yml`, `api/index.md`), and the new `docs.yml` workflow builds the site
  strictly (`--warningsAsErrors`) on every PR touching sources or docs, and deploys
  it to GitHub Pages on each `v*` tag.
- The strict build flushed out **31 broken documentation links** — including two
  references to the maintainers' private repository and four to a spec folder that
  no longer exists in the public tree — all fixed; ADR index pages (EN/FR) added.
- **Community templates**: structured issue forms (bug report with install-channel
  and reproduction fields, feature request), contact links routing questions to
  Discussions and security reports to SECURITY.md, and a pull-request template whose
  checklist mirrors what CI actually enforces (API freeze declaration, FR parity
  gate, CHANGELOG entry, executable bits, VFS-only I/O).
- 25 public entry-point types that had no XML `<summary>` (RaggableTree contracts,
  scripting JS builders, `BuilderValidationException`…) are now documented.

### Changed — Documentation debt cleared; FR/EN parity is now a CI gate (PUB-09)

- The examples catalog (`docs/reference/examples-catalog.md` + FR) is now editorial
  only: the generated, CI-checked `examples/INDEX.md` is the authoritative inventory,
  so the page no longer maintains counts or paths by hand (the old page announced
  104 examples with a table summing to 101 and folder casings that did not exist).
  It now also covers the RAG, RaggableTree, scripting, CLI-commands, local-embeddings
  and multi-file showcases.
- The last four missing French mirrors are delivered (`run-your-first-example`,
  `example-data-policy`, `hosting`, the example README template): `docs/` parity is
  green for the first time, and `scripts/check-docs-parity.sh` now runs in `ci.yml`
  on every push and PR — a missing mirror fails the build. CONTRIBUTING (EN/FR)
  states the CI-gate wording again.

### Security — Testcontainers 4.13.0 → 4.14.0 (test infrastructure only)

Clears the last build warning, NU1903: Testcontainers 4.13.0 pulled SSH.NET 2025.1.0
transitively, which carries a known high-severity advisory
(GHSA-q939-rpr3-3284); 4.14.0 depends on the fixed SSH.NET 2026.0.0. Test-only
dependency — nothing shipped in the NuGet packages or installers was affected.

### Added — The public API surface is frozen and enforced (PUB-05)

Ahead of the first public release, the API contract is now mechanical, not aspirational:

- **PublicAPI baselines** — every packable library carries `PublicAPI.Shipped.txt`
  (29 235 declared public APIs across 28 projects, source-generated members included)
  and an empty `PublicAPI.Unshipped.txt`, checked by
  `Microsoft.CodeAnalysis.PublicApiAnalyzers`. An undeclared public API addition or
  removal is a **build error** (`RS0016`/`RS0017` promoted via `WarningsAsErrors`).
  Executables (`src/apps/`) opt out — an app's surface is not a contract.
- **`[Experimental]` on the unstable surfaces** — 71 types now carry
  `ExperimentalAttribute` with stable diagnostic IDs, documented in
  `docs/reference/experimental-apis.md` (EN + FR): `ORKEXP001` A2A (pre-v1.0.1
  implementation), `ORKEXP002` Autonomous orchestration (budget, A2A channel, spawn),
  `ORKEXP003` corrective RAG (CRAG contracts), `ORKEXP004` MCP (pinned to `2024-11-05`
  until the protocol upgrade). Referencing them from outside the repository is a
  compile error to suppress explicitly; `src/`, `tests/` and `examples/` suppress the
  four IDs centrally because the framework wires its own experimental surfaces.
- **Versioning policy** — CONTRIBUTING (EN + FR) now states the contract: SemVer, a
  breaking change is any edit to `PublicAPI.Shipped.txt`, `[Obsolete]` ships at least
  one minor version before any removal, and no breaking change to a stable shipped API
  within the 1.x window.
- The 47 `InternalsVisibleTo` declarations were inventoried: 36 target test projects;
  the 11 production-to-production grants all belong to shared-kernel pairs already
  documented by ADR-002/003/006 (abstractions → implementation, Domain → Application/
  Infrastructure, Cli.Abstractions → Cli) plus two grants to the `orkeon` tool
  executable — kept, documented in the PUB-05 closure note.

### Changed — Complete NuGet package metadata (PUB-04)

Every one of the 28 packable projects now ships presentation-grade metadata:

- **Package icon per family** — `assets/nuget/` holds one 128 px icon per `src/` zone
  (core, cli, scripting, analyzers, tools, rag, analysis, generators, hosting, plugins);
  a per-zone `Directory.Build.props` declares the family and the central props pack it
  as `icon.png`, so every `.nupkg` carries its zone's icon with a single `<PackageIcon>`.
- **A dedicated README per package** — each project directory now has a short `README.md`
  (role, install, doc links) packed via `PackageReadmeFile`; the five projects that
  already had a rich developer README ship that one. The shared generic `nuget/README.md`
  that seven packages used to duplicate is removed.
- **`PackageReleaseNotes`** — centralized in `src/Directory.Build.props`, pointing at
  this CHANGELOG.
- **`LICENSE.md`** — copyright aligned with the build props (`2024-2026`).

### Changed — NuGet.org publication wired for real (PUB-03)

`publish.yml` now pushes the three core packages (`Orkeon.Domain`, `Orkeon.Application`,
`Orkeon.Infrastructure` — the v1 set of `docs/reference/publication-matrix.md`) to
**NuGet.org** on a `v*` tag. Authentication is **Trusted Publishing (OIDC)**: a nuget.org
policy for `Orkeon/orkeon` + `publish.yml` lets `NuGet/login` exchange the job's OIDC token
for a short-lived key — no long-lived API secret exists anywhere. The steps are gated on the
`NUGET_USER` repository variable: until the owner finishes the nuget.org setup, they warn
and no-op instead of failing the release. The workflow
also **refuses a tag that does not match the `src/Directory.Build.props` version** — the
guard that makes the 0.9.1-beta silent-skip incident (rc tags re-packing an unchanged
version, `--skip-duplicate` skipping every push) structurally impossible. The README NuGet
badge, which pointed at a package that does not exist on nuget.org yet, is replaced by a
GitHub release badge until the first real push restores it.

### Changed — `Orkeon.Cli.Scripting` renamed to `Orkeon.Cli.Commands.Scripting` (ADR-007, decision D3)

The library of TypeScript-scripted interactive CLI commands loses its near-anagram name
(`Orkeon.Cli.Scripting` vs `Orkeon.Scripting.Cli`): project, PackageId, assembly, root
namespace and test project are now `Orkeon.Cli.Commands.Scripting(.Tests)`. The `orkeon`
dotnet tool (`Orkeon.Scripting.Cli`) keeps its name — its PackageId is the install command.
No published package carried the old name, so nothing breaks outside this repository;
in-repo consumers were updated in the same change. This supersedes ADR-004 and lifts the D3
gate in `docs/reference/publication-matrix.md`. Entries below in this Unreleased block use
the new name even where the work predates the rename.

### Added — Orkeon Studio: a graphical way in, on Windows and Linux

Configuring Orkeon and launching a crew no longer requires a terminal. **Orkeon Studio**
ships as three applications over one shared core (`Orkeon.Studio.Core`, which holds the
appsettings model, the target detection and the `orkeon run` argument building — a feature
absent from the core exists in no UI):

- **`orkeon-studio`** — a WPF desktop app for Windows, two tabs (settings editor, crew
  launcher);
- **`orkeon-studio-config`** — a full-screen Terminal.Gui editor for the settings file:
  provider presets, model and endpoint, and the VFS mount table, saved in the exact form
  `FileSystemMount.Parse` reads back;
- **`orkeon-studio-run`** — the crew launcher in the terminal: pick a `config.yaml`, a crew
  directory or a `.ork.ts` script, set the options (`--validate` included), follow the output
  live, and cancel a run — the process is terminated and the exit code (130 on cancellation)
  is reported, with the UI still alive.

None of them is a second product: they edit the same `appsettings.json` `orkeon init`
writes, and they launch crews by executing the co-installed `orkeon` binary, so the CLI and
Studio are interchangeable on the same machine at any point.

**Distribution follows the platform, not the wish list.** The `win-x64` zip and the MSI carry
`orkeon-studio` (the MSI adds an "Orkeon Studio" **Start-menu shortcut**, its only
MSI-specific authoring); the Debian package and the Linux archives carry
`orkeon-studio-config` and `orkeon-studio-run` (`/usr/bin/orkeon-studio-{config,run}` on the
`.deb`, launcher symlinks in `<prefix>/bin` from a tarball); the macOS **onboarding** channel
— the `orkeon-cli-*-osx-*` tarballs and Homebrew — stays **CLI-only in V1** (the multi-app
`orkeon-*-osx-*` archives carry the two TUIs like every other RID, untested there).
Linux archives and the `.deb` hardlink-deduplicate their payload at staging
time (`scripts/hardlink-dedup.sh` — tar and dpkg both preserve hard links, and
gzip alone cannot deduplicate across files): the byte-identical .NET runtime
and shared Orkeon assemblies of the self-contained apps are stored once
instead of once per app, cutting the cli linux tarball from 176 MB to 104 MB
with strictly identical extracted content.
Every Studio app is published self-contained like the CLI itself, which keeps the `.deb`'s
`Depends` free of any `dotnet-runtime-*` — the onboarding channel's invariant. The app table
in `package-installers.sh` / `.ps1` gained a RID-filter column for this (WPF cannot target
non-Windows RIDs), and `SHA256SUMS` is unchanged in shape: same archive names, richer
contents.

The release smokes assert all of that on real runners rather than at packaging time:
`orkeon-studio --smoke-exit` opens the WPF window, lets it render and exits 0 on
`windows-latest` (both channels — zip and MSI — through one shared assertion file); the
`.deb` and linux tarball smokes require both TUI launchers and run `--version` on each with
**no terminal at all** (stdin from `/dev/null`, both streams redirected), which is the
contract that keeps them scriptable; and the macOS smoke asserts the **absence** of anything
named `orkeon-studio*`, so the day the RID filter regresses, CI fails instead of a Mac user.

### Added — `orkeon run <directory>`: multi-file crews are a first-class target

A crew no longer has to be a single file. `orkeon run` (and every runner's `-c/--config`)
now accepts a **directory**: `config.yaml` for the crew settings, one agent per file under
`agents/`, one task per file under `tasks/`, each file-name stem being the entity id — the
layout `YamlCrewDefinitionLoader.LoadFromDirectoryAsync` already understood, which until now
no CLI could reach because the dispatch was by file extension only. The legacy flat triplet
(`crew.yaml` + `agents.yaml` + `tasks.yaml`) is accepted from a directory too, and every
option behaves identically on a directory and on a file (`--settings`, `-V/--var`,
`--initial-context`, `--mount`, `--validate`, `--verbose`, `--llm-log`). A `.yaml` path
passed directly follows exactly the path it always did.

The classification is explicit rather than convenient: a directory holding both a YAML
layout and a scripting entry point — any `*.ork.ts` or `*.ork.js` sitting directly in it,
whatever the file is called — is refused with both candidates named, and a directory with no
recognized layout is refused with the list of what was searched — no silent precedence, and
a lone script is never executed just because it was the only thing in the folder. The
directory is mounted read-only in the VFS as itself, not as its parent, so a crew directory
opens no wider a surface than a crew file. `examples/crew-multifile/` is the runnable
reference (`orkeon run examples/crew-multifile --validate`).

### Added — macOS channel: osx CLI tarballs, Gatekeeper handling, Homebrew formula

macOS joins Windows and Debian as a first-class install target. The release now carries
`orkeon-cli-<version>-osx-arm64.tar.gz` and `-osx-x64.tar.gz` — the `orkeon` CLI alone,
self-contained and tree-sitter-pruned like every other CLI package, cross-published from the
Linux runner (the apphosts ship in the SDK packs, the natives come from NuGet, and esbuild is
fetched per-RID as `@esbuild/darwin-{arm64,x64}`). No .NET install is required on the Mac.

`install.sh` gained a Darwin-only block that removes the two ways an unsigned binary fails on
macOS. It clears `com.apple.quarantine` from the installed tree — a browser download tags
every extracted file with it, which is what produces *"cannot be opened because the developer
cannot be verified"*, and running the installer is the user's own act of trust. It then runs
`codesign -v` over the bundled Mach-O files and ad-hoc re-signs **only** those that fail,
because Apple Silicon refuses to load an unsigned Mach-O while a valid publisher signature
(onnxruntime's, for instance) must never be replaced by an ad-hoc one. Both halves degrade
quietly when `xattr` or `codesign` is unavailable, no individual failure aborts the install,
and the block is skipped outright off Darwin — Linux behaviour is unchanged. A new blocking
`smoke-macos` job (`macos-latest`, Apple silicon) installs the `osx-arm64` tarball on a real
Mac and walks init → doctor → run → rag → uninstall on it, which is what actually proves the
signing story: a native library killed at load time fails there instead of in a user's
terminal.

A Homebrew formula ships in the repository at `installers/homebrew/orkeon.rb`: a binary
formula that fetches the tarball for the machine's architecture (`on_arm` / `on_intel`),
installs the payload under the Cellar's `libexec`, and writes a `bin/orkeon` wrapper pointing
`ORKEON_ESBUILD_PATH` at the bundled esbuild. Its `test do` runs `orkeon doctor`, not
`orkeon --version` (which exits `1`). `scripts/update-homebrew-formula.sh` regenerates the
version and both url/sha256 pairs from a release's `SHA256SUMS` — idempotent, and it refuses
to write when the formula's structure no longer matches what it knows how to rewrite. The
`Orkeon/homebrew-tap` repository is **not published yet**, so `brew install orkeon` does not
resolve; until it is, the two `sha256` values are explicit placeholders that fail verification
rather than fetch anything unverified.

### Added — `orkeon init` and `orkeon doctor`

Two new verbs make the first ten minutes on a fresh machine self-service. `orkeon init` is
a wizard over five providers — `ollama`, `docker-model-runner`, `openai`, `custom`, `none` —
that writes an `appsettings.json` at the global per-user path (below), then probes the endpoint
to confirm it answers. Every prompt has a flag, so it scripts end to end:
`--provider`, `--base-url`, `--model`, `--api-key-env` (the recommended way to carry a key),
`--api-key` (inline, discouraged), `--path`, `--force`, `--no-probe`. With a non-interactive
stdin and no `--provider`, it refuses rather than hanging. `orkeon doctor` runs nine checks —
`dotnet-runtime`, `appsettings`, `llm-config`, `llm-reachability`, `esbuild`,
`local-embeddings`, `onnx-reranker`, `tree-sitter`, `workspace-write` — prints them as a
✅/⚠️/❌ table, exits `1` as soon as one fails (warnings stay green), and emits a
`[{check, status, detail}]` array under `--json` for CI.

### Added — global per-user configuration path in the settings resolution

Settings resolution gains a fourth step: after the local `appsettings.json` and the walk up
the parent directories, and before the env-vars-only fallback, the CLI now reads
`%APPDATA%\Orkeon\appsettings.json` on Windows and `~/.config/Orkeon/appsettings.json` on
Linux/macOS — the file `orkeon init` writes. An installed `orkeon` therefore works from any
working directory, and configuration never lives in the install directory, which every
(re)install deletes outright.

### Changed — an unconfigured LLM warns instead of silently echoing

Building a runner host with no `Llm` section still falls back to the echo provider, but it now
says so once on stderr — *"No `Llm` section configured — falling back to the echo provider
(`<undefined-llm>`). Run `orkeon init` to create a configuration, or set
`ORKEON_Llm__BaseUrl` / `ORKEON_Llm__Model`."* The fallback itself is unchanged (it is what
makes the scripting demos runnable with no key and no server); what changes is that a crew
replaying its own prompts can no longer be mistaken for a crew talking to a model.

### Changed — every publish prunes the unused tree-sitter grammars

**Behaviour change.** `TreeSitter.DotNet` ships one native library per supported grammar (31,
~69 MB on win-x64) while Orkeon only loads the seven declared in `LanguageRegistry`. A
`PruneUnusedTreeSitterGrammars` target in `src/Directory.Build.targets` — imported wholesale by
`examples/Directory.Build.targets` — now drops the rest from `ResolvedFileToPublish`, so **every
`dotnet publish` under `src/` and `examples/` emits 7 grammars instead of 31**, not just the
release archives. `dotnet build` is untouched. Opt out with
`-p:OrkeonPruneTreeSitterGrammars=false` (useful when diagnosing a grammar-loading problem).
Safe by construction — `LanguageRegistry.Create` throws for any language outside the registry —
and the whitelist ↔ registry agreement is pinned by `TreeSitterGrammarPruningTests`.

### Added — Windows and Debian install channels; hardened `install.ps1`; runtime detection in `install.sh`

Three new release artifacts sit next to the existing multi-app archives, all self-contained:
`orkeon-cli-<version>-win-x64.zip` (the `orkeon` CLI alone, with `install.ps1`),
`orkeon_<version>_amd64.deb` (installable with `sudo apt install ./orkeon_*.deb`; depends on
system libraries only, through libicu/libssl alternations covering Debian 12/13 and Ubuntu
22.04→26.04, never on `dotnet-runtime-*`), and `orkeon-<version>-win-x64.msi` (WiX, per-user,
no administrator rights — one Windows channel at a time, the MSI refuses to install over a zip
install). `release.yml` is restructured into `installers → {smoke-windows, smoke-deb, msi} →
release`, so nothing reaches the Release until it has been installed and exercised on a real
Windows runner and a stock Ubuntu image — the `msi` job carries its own
`msiexec /i /qn` → `orkeon doctor --json` → `msiexec /x /qn` smoke — and it now also runs on
`workflow_dispatch` (everything except the publication).

`install.ps1` gained an "Apps & features" entry (with a working `UninstallString`), rescues an
`appsettings.json` left in a previous install directory into `%APPDATA%\Orkeon` before the
delete-and-replace, preserves the `RegistryValueKind` of the user `PATH` instead of flattening
`REG_EXPAND_SZ` to `REG_SZ`, and only checks for a .NET runtime when the payload actually needs
one (keyed on `hostfxr.dll`). `install.sh` gained the POSIX mirror of that check: it looks for
a framework-dependent app under `libexec/` (no `libhostfxr.so`/`.dylib`), then for a
`Microsoft.NETCore.App 10.x` runtime on the `PATH` or under `DOTNET_ROOT`, and when it finds
none prints the exact commands per distribution — `sudo apt install dotnet-runtime-10.0` on
Ubuntu 25.10+, the `packages.microsoft.com` repository registration on Debian and Ubuntu LTS,
`dotnet-install.sh --runtime dotnet --channel 10.0` under `$HOME` without sudo — and repeats
the reminder at the end. It never installs a runtime, adds a repository or calls sudo on the
user's behalf, and the warning never blocks the install.

### Fixed — `package-installers.sh` aborted at the checksum step with a single `--rids`

The final `ls *.tar.gz *.zip *.deb | xargs sha256sum` left one glob unmatched whenever the run
targeted a single RID; under `set -o pipefail` the failing `ls` took the whole pipeline down
and `set -e` aborted the script — after every archive had already been built. The `ls` is now
wrapped in `{ …; || true; }`, and `*.deb` is part of the glob so `SHA256SUMS` stays complete
when `package-deb.sh` has dropped its package in the same output directory.

### Added — shell_command: bidirectional VFS path rewriting

`shell_command` now speaks virtual paths in both directions, so a coding agent can run
`dotnet build /workspace/App.sln` instead of failing on a path that only exists in the
VFS. Inbound, every argument that names a mount-prefixed virtual path — including the
embedded `--out=/workspace/dist` form (split at the first `=`) — is resolved
virtual→physical through `IFileSystemService.ResolveAndValidate` before the process
starts, with a path-boundary check (`/workspaces` never matches mount `/workspace`); a
denied path fails the call with the redacted denial reason instead of reaching the
process verbatim. Outbound, stdout/stderr are rewritten physical→virtual before
truncation (success and timeout paths alike) using a per-call table built by resolving
each mount root — longest physical base first, backslashes normalized to `/` inside the
rewritten path token — so the model only ever sees virtual paths and stops leaking
physical host paths into its own follow-up `file_read` calls. Scope: `AgentFacing`
mounts, ordinal matching (re-cased Windows output is a documented limitation); with no
mounts configured both passes are exact no-ops.

### Added — shell_command: configurable allowlist

Two new config keys shape the executable allowlist without code changes:
`Orkeon:Tools:Shell:ExtraAllowedCommands` (string array) is ADDITIVE on top of the
default allowlist — the recommended way to allow `make`/`cargo`/etc. for a trusted
coding-agent host; it composes with `AllowInterpreters` (new `extraAllowedCommands`
ctor parameter, unioned after the base list is built). `Orkeon:Tools:Shell:AllowedCommands`
(string array) is a full verbatim REPLACEMENT mapping to the existing `allowedCommands`
ctor parameter — per that contract it cancels `AllowInterpreters` and re-enables the git
read-only subcommand restriction. An absent or empty section binds to `null`, never to
an empty array (which would block every command), so defaults are unreachable by
accident.

### Added — configurable LLM retry budget (default 10) + visible reconnection feedback

`Llm:MaxRetries` (the dormant `LlmConfig.MaxRetries`, never consumed until now) drives
BOTH HTTP paths: the buffered Polly policy (`GetLlmApiPolicy`, previously hardcoded at
5) and the streaming connect-phase loop (previously hardcoded at 3 attempts). Default
raised from 3/5 to **10** (`LlmDefaults.DefaultMaxRetries`) with every wait capped at
30 s (`ResiliencePolicies.LlmRetryDelay` — linear ×1/×2, then ×3 exponential, capped),
so the ladder degrades to a bounded cadence instead of 3⁸ seconds. Both config-binding
hosts read the key (`ConfiguredLlmProviderBootstrapper` for the ConsoleApp REPL,
`RunnerHost` for runner hosts), clamped at 0; the cap is applied before the `TimeSpan`
conversion (an arbitrarily large configured budget never overflows mid-retry) and a
server `Retry-After` is now capped at the same 30 s on the buffered path, matching the
streaming path.

What makes a 10-retry budget acceptable on an interactive turn is that it is now
VISIBLE: a new `ILlmRetryObserver` port (Application) receives every scheduled retry
wait and the final settle; `HttpLlmProviderBase.RetryObserver` fires it from both
paths (never on mid-stream failures, which are still not retried). The CLI implements
it with `LlmRetryProgressObserver`: the status line shows
`✳ Reconnecting to api.moonshot.ai… retry 4/10 in 8s — <reason>` through the existing
`ProgressBroker`, and the ambient `CommandInstance` gets the same line for
`ps`/`inspect`/the agents pane. Settling clears only the banner the observer raised
(label-guarded) — never a crew's own progress. No observer registered = behaviour
unchanged (retries only logged).

### Fixed — transcript errors: one actionable line, never a stringified stack

A crew failure travels as
`PromiseRejectedException(ObjectWrapper(AggregateException(HttpRequestException(SocketException))))`
and its `Message` embeds the full stringified stack — which the REPL used to dump
verbatim into the transcript (the live `/analyze` incident: ~40 lines of .NET frames
for one DNS hiccup). New `ConciseErrors` helper (`Orkeon.Cli.Commands.Scripting`) unwraps the
wrapper layers (JS rejection → carried CLR exception, `AggregateException` flatten,
`TargetInvocationException`) and keeps the first line of the root cause —
`✗ analyze failed: Resource temporarily unavailable (api.moonshot.ai:443)`. Applied at
every transcript-facing site (`ScriptHostFacade` crew failures, `ScriptCommand`
dispatch/completed rejections, `CommandDispatchService` instance failures); the full
exception still goes to the logs at each site.

### Added — LLM streaming: connect-phase retry for transient failures

The buffered HTTP path runs under the Polly `GetLlmApiPolicy`, but
`SendStreamingRequestAsync` was a single bare `SendAsync` — one transient socket
failure killed the whole turn. It now retries the CONNECT/headers phase itself
(3 attempts, 0.5 s/1 s backoff, `Retry-After` honoured capped at 30 s) on transport
errors, client-side connect timeouts, and retriable statuses (408/429/5xx). Once
headers are handed to the caller, a mid-stream failure is never retried — replaying a
partially-consumed stream is the caller's decision. Non-transient statuses (401…)
return immediately, unretried.

### Fixed — TUI: a line typed before the runner's first read was silently dropped

The split-pane input field is live from the first frame, but the scripted-commands
runner only starts reading after its startup script load (57 commands ≈ 30–60 s of
discovery → esbuild → evaluate). A line submitted in that window was echoed to the
transcript and then **discarded** — the live "que fait-on ?" incident: the free-text
request looked accepted and nothing ever happened. `ReplPaneView` now buffers
type-ahead submissions in a FIFO queue and delivers them to subsequent
`ReadLineAsync` calls — the same type-ahead semantics a plain terminal gives for
free. Pinned by three `ReplPaneViewTests` (buffered delivery, FIFO order, live read
still wins).

### Fixed — scripted commands: `async dispatch` / `async completed` now awaited deterministically

`ScriptCommand` unwrapped an async `dispatch`'s promise with the synchronous
`UnwrapIfPromise` (blocking the engine-lock thread until settlement) and the
`completed` drain did the same. Both now await `UnwrapIfPromiseAsync` — same pattern
as the sync-handler path — and surface a rejected dispatch/completed promise as the
same `Error: …` console line as a thrown one, instead of relying on Jint's blocking
unwrap semantics. Pinned by a dispatch-with-pending-promise integration test (the
`/assistant` shape since B-5: await session state, then post).

### Added — end-to-end progress channel for long CLI operations

A `ProgressBroker` singleton (`Orkeon.Cli.Commands.Scripting.Progress`, registered by
`AddScriptCommands`) now carries a live `{label, step/total | percent, message}`
snapshot from whoever is doing long work to whoever renders it. Three publishers:
`ctx.progress(...)` handles from command scripts (which also stamp the ambient
`CommandInstance.ReportProgress` — the field `ps`/`inspect` exposed since design §6 but
nothing ever wrote); a new host tool **`progress_report`** so CREW scripts — which have
no `ctx.progress` — can report through the `tools` global (`Tools.progressReport`);
and an optional `IProgress<IndexBuildProgress>` hook on `RaggableEnrichmentServices`
notified by `RaggableTreeBuilder` per phase and per parsed file (null by default —
zero cost when unwired; the ConsoleApp routes it to the broker as "Indexing codebase").
The TUI status line renders the snapshot as
`✳ Compacting conversation… ▰▰▰▱▱▱▱▱▱▱ 34% (12s)` (indeterminate operations show
elapsed + message instead of a bar), including for background crews that run detached
from the REPL's own turn; the agents pane swaps a running row's intent for its live
progress. `ProgressAmbient` (AsyncLocal) links detached `post`/`postWork` flows to
their instance; completion clears the broker slot by ticket so one instance can never
erase a newer operation's bar.

### Added — `spinnerVerbs`: configurable status-line verbs + spinner animation

The status line's verb rotation ("thinking verbs" in the tweakcc vocabulary) is now
configurable: `TerminalGuiOptions.SpinnerVerbs` seeds a boot-time list (bound from
`Orkeon:Cli:Tui:SpinnerVerbs` in the ConsoleApp), and the live
`TuiIntegration.SpinnerVerbs` delegate — wired by the ConsoleApp to the scripted `/config`
layers (`config_map` session state over `/workspace/.orkeon/config.json`) — wins over
it without a restart. The verb re-draws every fifteen seconds on long turns
(`StatusLineFormatter.VerbFor`), the leading glyph animates through spinner frames
(`✢ ✳ ✶ ✻`, ASCII `| / - \`) at four steps per second (`SpinnerFrame`), and the
status-line timer tightened from 1 s to 250 ms accordingly. Defaults unchanged: the
six Orkeon gerunds.

### Fixed — agents pane: live work only, finished agents leave immediately

`TuiFidelityWiring.BuildAgentRows` collapsed every non-running instance to `idle`, so a
finished crew was indistinguishable from a stuck one (the exact confusion of the
2026-08-06 captures). Per the user ruling that followed — `idle` means *waiting*, not
*finished* — the pane now shows LIVE work only: a finished agent's row disappears at
once (no retention window, no terminal badges; `ps`/`inspect` stay the audit trail),
and `idle` never renders at all. `● main` (filled bullet, no metrics) appears only
while at least one delegated agent runs — with nothing delegated the pane collapses,
matching the reference. Delegated rows render hollow (`○`) with the live
`elapsed · ↓ tokens` pair.

### Added — `ILlmUsageSink`: per-call LLM usage events, per-agent token attribution

New Application port `ILlmUsageSink` (mirror of `ILlmDeltaSink`, same plumbing chain
`JsEngineFactory → crewBuilder → JsCrew → JsLlmFacade`): every `ctx.llm.*` path —
`complete`, `chat`, `extract`, `decide`, `stream` (both variants), and each `act`
iteration (buffered or streamed, counted exactly once) — reports a `CostUsageEvent`
carrying crew/agent/provider/model and the token split (a total-only response lands on
`CompletionTokens` so `Prompt + Completion == TokensUsed` — the pricing registry then
prices that total at the output rate, a deliberate upper bound: conservative for
budgets, an overestimate for cost reporting on split-less providers; a response with
no usage at all reports nothing — "no usage" ≠ "zero tokens"). `extract` reports
before its JSON parse, so a prose reply that throws still counts the paid tokens. Nothing fed `ICostBudgetManager`
before this: the REPL's session token readout summed an event stream no one produced.
`AddScriptCommands` registers `InstanceAttributingUsageSink`, which forwards to the
cost manager (fixing that readout and `/cost`) AND credits the `CommandInstance`
ambient at call time (`ProgressAmbient`, the progress channel's AsyncLocal) — so the
agents pane's `↓ NN.Nk tokens` is now that agent's real usage (`—` only when truly
unattributable). `CommandInstanceView` gains `tokens` (visible to `ps`/`inspect` and
the F4 detail; typed in `orkeon-cli.d.ts`). A sink that throws degrades to unobserved
usage, never to a failed LLM call.

### Added — agents pane: keyboard + mouse selection (F4)

The pane stays non-focusable at rest (its first live launch proved a focusable
read-only pane steals the prompt focus), but **F4** now enters an explicit selection
mode: ↑/↓ move a chevron cursor, **Enter** prints the instance's detail into the
transcript (state, intent, elapsed, progress, result/error — via the new
`TuiIntegration.DescribeAgent` delegate over `dispatch.get(ticket)`), **Esc** hands
focus back to the prompt. A mouse click selects a row without stealing focus; a
double-click opens the same detail. The hint bar advertises `f4 agents` only while the
pane has rows. (F4, not Ctrl+A: the focused panes' select-all already owns Ctrl+A and
global bindings fire before view dispatch.)

### Added — `ActOptions.system`: a real system prompt for scripted `act()` agents

`ctx.llm.act(prompt, { system })` now seeds a `role:"system"` message as the first
message of the tool-calling conversation (re-sent on every loop iteration). Until now
`act()` always sent a single user message, so a scripted agent could not have a system
prompt at all — identity and tool policy travelled inside the user turn with user-level
authority (the same authority as tool results, which also come back as user turns), and
the providers' native system handling (Anthropic top-level `system`, `cache_control`;
`PrependConfiguredSystemMessage` on the OpenAI-compatible providers) never fired. The
conversation-level message wins over `LlmConfig.SystemMessage` on every provider; the
option omitted keeps the historical single-user-message shape byte for byte.
(`JsLlmFacade.ResolveSystem`, `Typings/context.d.ts`.)

### Added — hybrid code search + edit↔search freshness in the RaggableTree (RAG×Tree)

`codebase_search` (and `IRaggableStore.SemanticSearchAsync`) fuses an embedding cosine
ranking with a **code-aware BM25** (camelCase/snake_case sub-tokens + whole identifier)
via Reciprocal Rank Fusion — `SemanticQuery.Mode` (`Hybrid` default / `Vector` /
`Lexical`), `SearchHit.MatchOrigin`. Pure vector missed `getUserById` when the query
said "fetch user", and an exact identifier could rank below prose; each half now covers
the other's blind side. With no embedder wired, `Hybrid` degrades to `Lexical` instead
of the historical silent empty (explicit `Vector` keeps that contract). The BM25/RRF
implementations are Analysis-native twins of the RAG's (`Bm25CodeIndex`, `RankFusion`) —
the dependency must keep pointing Rag → Analysis, never back.

Freshness (an agent that EDITS files invalidates its own index): `FileWriteTool` gains
an optional `IIndexInvalidation` hook (dirty-marking, O(1), same precedent as the
citation validator); `IndexFreshnessService` reindexes the dirty set ∪ the git
working-tree changes (shell edits) BEFORE a read tool answers — grouped, single-flight,
2 s clean-probe debounce, failure degrades to the stale index and keeps the debt.
`codebase_search` reports `refreshed_files`; `index_status` reports `dirty_count`/
`dirty_paths`. `InMemoryRaggableStore` gains a `ReaderWriterLockSlim`: a search running
DURING an incremental reindex no longer risks `InvalidOperationException` on the mutated
dictionaries. The freshness pass is the first real producer on `IRaggableTreeEventBus`;
`IGitDiffProvider` (never registered before — `incremental_reindex`'s commit-range path
could not resolve it) and the bus are now registered by `AddRaggableTree`, and
`IGitDiffProvider` gains `GetWorkingTreeChangesAsync`.

Orkeon.ConsoleApp wires `AddOrkeonRag` + `AddOrkeonRagTools`: `rag_search`/`rag_ingest`/
`rag_eval` are available to the scripted REPL, and `rag_search`'s `raggable-tree`
collection inherits the hybrid + freshness path. Live-validated end to end (an internal probe crew,
8/8): exact identifier ranks `hybrid`, write→search round-trip reports
`refreshed_files: 1`, RAG routing serves the code index.

### Changed — `ctx.llm.stream` now asks for usage and carries the reasoning channel (SCR-24)

`stream` went through `GenerateStreamingAsync`; it now goes through `ChatStreamingAsync`. Both read the same SSE stream, and the difference is what they ask for: the chat path sends `stream_options: { include_usage: true }`, without which most providers emit no usage chunk at all — so a streamed call had **no token accounting**. Measured on a live run whose two streamed requests were `{"model":…,"stream":true}` with no `stream_options`; they carried usage only because Moonshot volunteers it, and the same round on OpenAI would have reported nothing. The calls that stream are the long, expensive ones.

The plain path also dropped `delta.reasoning_content`, so a thinking model's stream is silent for as long as it thinks — round-41's deliverable 13 spent 22 673 of its 32 627 completion tokens reasoning, most of a nine-minute call in which "no chunk yet" and "the stream died" were the same observation.

- `stream(prompt)` keeps yielding strings — no contract change. What the chunks cannot carry is exposed on the returned object: **`usage`** (`{ promptTokens, completionTokens, tokensUsed, cacheHitTokens, model }`, `null` while the stream runs and `null` for good when the provider reported nothing) and **`reasoningChunks`**. Read them after the loop.
- Deliberately NOT callbacks. A callback has to be invoked from the stream's own thread, and Jint's `Engine` is single-threaded: the first cut of this did exactly that, and a measured run — seven area writers streaming concurrently — died of a `NullReferenceException` inside `ScriptFunction.Call`, with all seven writers falling back to a placeholder and the script stopping silently after assembling its document. CLR state read through interop runs on the engine's own thread.
- Reasoning progress is logged by the facade through the host logger every 200 deltas: a script cannot log it for itself, because while the model reasons its loop body never runs.
- `usage` is populated on the non-streaming fallback too, so "this provider does not stream" and "this provider reported no usage" stay distinguishable.

### Added — `rag.retrieve` / `IRagRetrievalCapable`: retrieval without the generation nobody asked for (SCR-24)

A caller that wants the retrieved passages rather than prose was still charged for a full grounded generation, because `IRagPipeline` exposed only `QueryAsync`. Measured on the 2026-08-04 gap round: seven `rag.query` calls whose generated answers were discarded **by design** cost 14 748 completion tokens — 68 % of them reasoning tokens — and 394 s of wall time, on top of retrieval that had already produced every citation the caller used. The generation stage is the expensive half of a RAG call and it is optional far more often than the API shape suggested.

- **`IRagRetrievalCapable` (`Orkeon.Rag.Abstractions`)** — opt-in capability, same shape as `IHybridSearchCapable`: `RetrieveAsync` runs `transform → retrieve → fuse → rerank → assemble` and stops. Declared as a separate interface rather than added to `IRagPipeline` because not every executor can honour it — the corrective graph interleaves evaluation with generation, so "retrieval only" is not a prefix of its run — and a caller must be able to ask instead of discovering the answer through an exception.
- **`StagedRagPipeline`** implements it; `QueryAsync` and `RetrieveAsync` now share one `RetrieveCoreAsync`, so the two cannot drift. The returned `RagAnswer` keeps the same shape (empty `Text`, populated `Citations`) and the trace carries a `generate` step saying the stage was skipped on purpose — an absent step would read as a trace from an older pipeline.
- **`rag.retrieve(question, options)`** in the scripting DSL, same signature as `rag.query`. On a pipeline that is not retrieval-capable it throws rather than falling back to `QueryAsync`: a silent fallback would charge exactly what the caller asked to avoid, with no way to tell.

### Fixed — the `system` role was flattened into the user message on every OpenAI-compatible provider (SCR-24)

`HttpLlmProviderBase.ChatAsync` flattens messages into one prompt shaped `"{role}: {content}"` per line, and `OpenAICompatibleProviderBase` took the structured chat path only when tools, tool-call metadata or a vision payload were present. A plain `system` + `user` conversation — the shape of the entire RAG generation stage, and of every LLM judge, retrieval evaluator and groundedness checker in this repository, none of which declares a tool — therefore reached the provider as a single `user` message whose text began with `system: `. A multi-turn history was concatenated the same way, so an assistant turn arrived as something the user claimed the assistant had said. Evidence: the run's exchange log, all seven RAG generations sent as `messages: [{ role: "user", content: "system: You are a retrieval-augmented assistant…" }]`.

- Every `ChatAsync` call with at least one message now takes the structured path. A lone user message was affected too (it went out as `"user: Hello."`), so no case is left on the flattening path; an empty or null array still delegates to the base, which turns it into an empty single prompt.
- `grammar` (GBNF) was emitted only by the single-prompt builder and is now written by both, so an option cannot appear or vanish with the number of messages sent.
- Around 400 mocked provider tests missed this: they assert on the response, and a mocked handler answers whatever it is sent. The new `OpenAICompatibleProviderBaseRoleFidelityTests` read the outgoing payload on the cases with no tool and no image, which was the remaining blind spot.

### Added — Declared provider capabilities, structured outputs and thinking on all 12 providers (LLM-02, LLM-03, LLM-04)

The audit's central finding was not that the wiring was missing but that its absence was **invisible**: `LlmResponseFormat` and `LlmThinkingConfig` cascaded correctly from crew → agent → task → script → call-site, yet only DeepSeek wrote `response_format` and only DeepSeek and Z.AI wrote `thinking`. On the ten other providers a YAML declaration was silently dropped. Closes G-15, G-16 and G-19.

- **`LlmProviderCapabilities` (Domain)** — each provider declares what its API really supports: `ResponseFormat` (`None` | `JsonObject` | `JsonSchema`), `Thinking` (`None` | `EffortOnly` | `Toggle` | `Budget`), `Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`. Exposed on `ILlmProvider` as a **default-implemented** member (like `BaseConfig`), so third-party providers and test doubles keep compiling, and a provider that declares nothing gets nothing written on its behalf.
- **The end of silent drops** — an option the caller declared that the provider cannot honour now produces an actionable warning naming the option, the provider and the remedy. This is the actual fix for G-15/G-16; the wiring below is the easy half.
- **The OpenAI dialect is written once.** `OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` went from a no-op hook to a capability-driven implementation. DeepSeek and Z.AI held two near-identical copies of the thinking translation and two of the `reasoning_content` extraction; both were refactored onto the base and shrank to what is genuinely theirs (DeepSeek's cache counters, Z.AI's `reasoning_tokens`). Their 33 + 15 existing tests pass unmodified.
- **Structured outputs on all 12 providers (G-15)** — `LlmResponseFormat` gains an optional `Schema` (`LlmJsonSchema { Name, Schema, Strict }`) and a `JsonSchema(...)` factory; `Type` is untouched, so existing configurations behave identically. Dialects: `response_format` (OpenAI-compatible family, with `json_schema` where the vendor validates it), `output_config.format` (Anthropic), `format` (Ollama, which also accepts a full schema). A schema handed to a provider that only guarantees well-formed JSON is **downgraded to `json_object` with a warning**, never in silence.
- ⚠️ **Anthropic has no schema-less JSON mode.** `output_config.format` takes exactly `type` (always `json_schema`) and `schema` — there is no equivalent of `json_object`, and `name`/`strict` do not belong there (`strict` exists, but on individual tools). A `json_object` request on Anthropic is therefore **reported**, not sent in a shape the API would reject. Supply a schema, or state the shape in the prompt.
- ⚠️ **The YAML allow-list is lifted.** `YamlCrewMapper` accepted only `text` and `json_object` and downgraded everything else to `null` — which would have discarded `json_schema` before it reached any provider. Unknown values now travel to the provider **with a warning**: a new vendor value works without a framework release, and a typo surfaces as a provider error rather than as nothing at all. Declare a schema with a `response_schema:` block (`name`, `schema`, `strict`) next to `response_format: json_schema`; a `response_schema:` on its own implies `json_schema`. The scripting DSL gains `withResponseSchema(name, schema, strict?)` on both the agent and task builders.
- **Thinking on all 12 providers (G-16, G-19)** — four dialects: `reasoning_effort` (+ the `thinking` block where the API has an explicit toggle) on the OpenAI-compatible family, `thinking: {type: "adaptive"}` + `output_config.effort` on Anthropic, `think` (boolean or effort level) on Ollama, `enable_thinking` + `thinking_budget` on Qwen's DashScope dialect. `LlmThinkingConfig` gains `BudgetTokens`, mapped **only** to Qwen — the `budget_tokens` shape found in many older sources is rejected with an HTTP 400 by the current Claude generation, so it is reported rather than sent.
- **`reasoning_content` extraction is generic**; its **replay** stays DeepSeek-only, driven by `ReplaysReasoningContent` — it is an API constraint of theirs (HTTP 400 without it), not a property of reasoning models, and Z.AI documents the opposite.
- Ollama's `format` and `think` are wired **on the current `/api/generate` path**: contrary to what the audit reported, both are accepted there, and only native tool calling actually requires the `/api/chat` migration.

### Added — `orkeon llm probe`, the provider campaign harness (LLM-08/C1)

Roughly 400 unit tests cover the LLM providers and **every one of them speaks to a mocked HTTP handler**. A mock proves the framework sends what we believe it sends; it cannot prove the vendor accepts it. Before this, only DeepSeek had archived real-execution traces.

- `orkeon llm probe --provider <name> [--model …] [--base-url …] [--modes M1,M8] [--archive <dir>]` runs the matrix's protocol modes against a live provider and prints a report ready to paste into the matrix journal, evidence level included. Currently exercises **M1** (single prompt), **M2** (multi-turn + system), **M3** (text streaming), **M4** (chat streaming), **M7** (thinking), **M8** (response format), **M12** (typed error on an invalid model) and **M13** (cancellation). M5/M6 (tool calling), M9 (vision), M10 (cache) and M11 (long context) need per-provider assets or a deliberately expensive call and are not covered yet — the harness names what it does not run rather than implying full coverage.
- **The API key is never a command-line argument**: it is read from an environment variable (`--api-key-env`, default `ORKEON_LLM_API_KEY`) and never appears in the report or the archive.
- A mode that a provider's declared capabilities make inapplicable is reported as such, not as a failure; a mode that throws is recorded as a finding rather than aborting the campaign, since the framework's contract is a typed error response, never a throw. A campaign with any failed mode exits non-zero.
- **The campaigns themselves are not run here.** They consume real credits on real accounts, so the decision to spend belongs to whoever owns them; the remaining LLM-08 work (running the campaigns, replaying experiment 08, filling the matrix) is unblocked but outstanding.

### Added — Azure v1 GA API and native Ollama tool calling (LLM-07)

The two gaps that needed a pipeline migration rather than a payload field. Closes G-06, G-10 and G-25.

- **Azure v1 GA API (G-06, G-25)** — `BuildEndpoint` was hardcoded to the dated, deployment-based shape (`/openai/deployments/{deployment}/chat/completions?api-version=2024-02-01`). Azure has served a **v1 GA** surface since August 2025 — `{endpoint}/openai/v1/chat/completions`, no `api-version` — which is the only path to the Responses API and to the non-OpenAI models Azure resells (DeepSeek, Grok); all of it was unreachable. Select it with `api_version: v1` (typed property or custom parameter). **The dated shape stays the default on purpose**: switching it would silently change the URL of every existing deployment-based configuration.
- ⚠️ **Ollama tool calling is now native (G-10)** — the provider targeted `/api/generate`, which has no tool support, so tool calling went through the text-fallback protocol. It now reaches `/api/chat` whenever the conversation declares tools, replays tool calls, or carries an image, and `LlmProviderFactory` wires it with the native OpenAI strategy. The response is reshaped once — Ollama returns `message.tool_calls` with `arguments` as a JSON **object** and no `choices` array, which is precisely why the framework's single parser could not read it — into the OpenAI body (`arguments` as a JSON **string**, positional call ids since Ollama issues none). Everything else keeps `/api/generate`, so NDJSON streaming and GBNF `grammar` are untouched and the text fallback remains in charge for models without tool support.
- **Ollama vision** — images travel in a bare base64 `images` array rather than as OpenAI content parts. Remote image URLs cannot be forwarded (the server never fetches them), so an image referenced only by URL is skipped rather than silently dropped.
- `docs/reference/limitations.md` no longer describes a limit that has been lifted; its Ollama entry now states what actually remains (no `/api/chat` streaming, bytes-only images, text fallback for tool-less models).

### Added — Anthropic parity, vision exposure and HuggingFace routing (LLM-05, LLM-06)

- **Native SSE chat streaming on Anthropic (G-20)** — `ChatStreamingAsync` used to fall back to the base class's buffered emulation, which waits for the whole answer before emitting anything: no token ever arrived early on Claude. It now parses the Messages API event stream directly, emitting `ContentDelta`, `ReasoningDelta` (extended thinking) and a terminal `Completed` whose response is indistinguishable from the buffered one.
- **Anthropic cache metrics (G-21)** — `cache_creation_input_tokens` and `cache_read_input_tokens` feed the typed `CacheHitTokens` / `CacheMissTokens`, so `CacheHitRatio` is finally computable on Claude. The three input counters do not overlap, so `PromptTokens` is their sum; absent counters stay `null` (unmeasured, not zero).
- **Explicit prompt caching (G-17)** — new `LlmCacheConfig` value object, cascaded like `LlmThinkingConfig` (crew → agent → task → call-site, `cache:` block in YAML). Anthropic's cache is explicit: without a `cache_control` breakpoint **nothing is cached**, so a long system prompt is re-billed in full on every turn — Orkeon read the metrics but never placed a breakpoint. Off by default, since a breakpoint changes what the vendor stores and how the call is billed. Marking the system prompt promotes it to the content-block form the API requires; the tools breakpoint lands on the last tool, which covers the whole catalogue.
- **Vision exposed on the 8 remaining capable providers (G-18)** — Azure, Groq, Together, Mistral, Qwen, Kimi, Z.AI and HuggingFace declare the capability and inherit the `image_url` composition from the base; no per-provider override. Text-only calls emit exactly the payload they did before (asserted). Ollama stays out on purpose — it takes a base64 `images` array rather than OpenAI content parts, which belongs with the rest of its request-shape work. DeepSeek has no vision model and degrades an image message to its text fallback.
- **HuggingFace provider-selection suffixes (G-23)** — `:fastest` / `:cheapest` / `:preferred` and partner pinning (`:groq`) are the only cost and latency lever on Inference Providers. The suffix already travelled to the wire; what was missing was any way to know it was wrong. Added `WithRoutingPolicy` / `WithPartner` helpers and validation that **reports** an unrecognised suffix while still forwarding it — the partner list moves faster than this framework releases.
- **Guard on DeepSeek's Anthropic-dialect endpoint (G-22)** — `api.deepseek.com/anthropic` speaks the Messages API, but host inference matched `deepseek.com` and routed it to the OpenAI-compatible provider, producing malformed requests whose error surfaced far from its cause. It now fails immediately with a message naming the cause and the fix.
- **G-24 (Groq server-side tools) is ruled out with a motive, not left pending.** `browser_search` and `code_interpreter` execute on Groq's side, so they pass through neither `IBaseTool`, nor the agent loop, nor the framework's validation, rate limiting and telemetry. Exposing them is a tooling-architecture decision — how they coexist with Orkeon's tool registry and what gets traced — not a provider-support gap. The same reasoning covers the other vendors' server-side tools.

### Changed — LLM provider defaults, host routing and the Azure config guard (LLM-01)

First sheet of the LLM provider remediation plan, closing gaps G-01→G-05, G-07→G-09, G-11, G-12, G-14 and defect D-01 of the 2026-07-27 audit. **Four providers out of twelve failed with their out-of-the-box configuration**; four more targeted a superseded generation.

- ⚠️ **Default models changed — this changes the behaviour of every configuration that does not specify a model.** Each identifier was confirmed on the vendor's official documentation on 2026-07-27: OpenAI `gpt-4` → **`gpt-5.6-sol`** (`gpt-4` reaches end of life 2026-10-23 and caps context at 8 192 tokens), Anthropic `claude-3-5-sonnet-20241022` → **`claude-sonnet-5`** (retired 2025-10-28), DeepSeek `deepseek-chat` → **`deepseek-v4-flash`** (retired 2026-07-24), Kimi `moonshot-v1-8k` → **`kimi-k2.6`** (the `moonshot-v1-*` series sunsets 2026-08-31), Qwen `qwen-turbo` → **`qwen3.7-plus`** (absent from the catalogue), Mistral `mistral-large-latest` → **`mistral-medium-3-5-26-04`**. `LlmDefaults.DefaultModelName` — the fallback of `LlmConfig.Model` itself — moves with the OpenAI default. Pin a model explicitly to keep the previous behaviour.
- ⚠️ **Default endpoints changed** — HuggingFace `api-inference.huggingface.co` → **`router.huggingface.co`** (the old host is gone, so the provider could not work at all), Kimi `api.moonshot.cn` → **`api.moonshot.ai`** (the mainland host was the default for every account, including international ones; set `BaseUrl` explicitly for a mainland account).
- **Every default now lives in `ProviderDefaults`** — Groq and HuggingFace held theirs inline — and a pinning test asserts the model and endpoint of all twelve providers, read from live instances rather than from constants. Drift becomes a failing test, not a silent change.
- **International hosts are routed to their dedicated provider** instead of falling back to the generic OpenAI one: `api.moonshot.ai` (G-11), `dashscope-intl.aliyuncs.com` and the per-workspace `*.maas.aliyuncs.com` hosts (G-12). `router.huggingface.co` was already matched by the existing `huggingface.co` rule — the audit's G-13 was a false positive, and is now pinned by a test.
- **Mistral routing trap fixed (G-14)** — `InferFromModel` routed *every* `mistral*` model to Ollama, so a cloud identifier such as `mistral-medium-3-5-26-04` was sent to `localhost:11434`. Only the bare `mistral` and its Ollama tags (`mistral:7b`) stay local; versioned identifiers, including `ministral-*`, now reach the Mistral cloud. Setting `BaseUrl` still wins over model-name inference.
- **Azure configuration guard aligned across all four entry points (D-01)** — `ChatStreamingAsync` was not overridden, so with a missing `BaseUrl` it reached `BuildEndpoint` and threw a `NullReferenceException` where the other paths returned a typed error. It now emits the standard `Completed` event carrying that same error. `GenerateStreamingAsync` still ends in an empty stream — `IAsyncEnumerable<string>` has no error channel — but logs it instead of failing silently.
- **Cost tracking follows** — `gpt-5.6-sol` / `-terra` / `-luna` and `claude-opus-5` / `claude-sonnet-5` registered in `ModelPricingRegistry`; `gpt-4` keeps its own entry so configurations that pin it still produce a real cost.
- **`LlmConfig.Gpt4()` → `LlmConfig.WithDefaultModel()`** (and `Gpt4WithSecret` → `WithDefaultModelSecret`). These factories have always returned the *platform default* model, not literally `gpt-4`; with the default moved, the old names became actively misleading. The former names remain as `[Obsolete]` forwarders with identical behaviour — pass `"gpt-4"` to `LlmConfig.Create` if you really want that model.

### Fixed — post-RAG coherence audit (2026-07-26)

- **`LlmProviderToChatClientAdapter` honors the standard `ChatOptions.ResponseFormat`** — the adapter only read the `LlmChatOptionsKeys.ResponseFormat` AdditionalProperties key stashed by the orchestrator, so callers built on plain Microsoft.Extensions.AI options (RAG retrieval evaluator, groundedness checker, query complexity classifier setting `ChatResponseFormat.Json`) never reached providers wiring `response_format` (e.g. DeepSeek `json_object`). The adapter now falls back to `ChatOptions.ResponseFormat` when the key is absent (the explicit key keeps priority) — the JSON constraint was already enforced by strict prompts + tolerant parsing, this makes the API-level guarantee real on the providers that support it.
- **The corrective mechanism test now locks the rank-1 claim** — `CorrectiveRagMechanismSlowTests` asserted only that `notes-power.md` was cited; it now also asserts it is the **first** citation, matching the wording in the RAG-06 task sheet and the eval README.
- Documentation drift cleanup: version references aligned on `src/Directory.Build.props` (0.9.2-beta) across `README(.fr).md`, `CLAUDE.md`, `limitations.md`, `publication-matrix.md`; `publication-matrix.md` workflow narrative matched to reality (all NuGet pack/push lives in `publish.yml` → GitHub Packages; nothing on NuGet.org); `CONTRIBUTING(.fr).md` no longer claims a CI parity gate that was never wired; ADR-006 amended with the RAG-06 decisions (CRAG on `StateGraph`, web-fallback policy/transport split, `corrective` preset, no separate `rag-adr.md`); `docs/INDEX.md` links `rag-pipeline.md`; `limitations.md` + `opt-in-subsystems.md` document the ONNX requirement (`balanced`/`quality`/`adaptive`) and the double-opt-in web fallback; French mirror `docs/fr/architecture/rag-pipeline.md` added; eval README header corrected to 9 cases / 12 documents; `Orkeon.Tools.Rag` NuGet description lists its three tools; stale "lands with RAG-0x" comments rewritten in delivered code.

### Added — Corrective RAG & vitrine (RAG-06): CRAG graph on `StateGraph`, `corrective` profile, opt-in web fallback, examples

The showcase piece of the RAG plan (guide §8): Corrective RAG built on Orkeon's own Graph orchestration mode — the corrective engine *is* a Domain `StateGraph`, RAG demonstrates the `Graph` mode and vice versa — plus the vitrine layer (docs, three runnable examples, final all-profile evaluation).

- **CRAG graph pipeline (C1)** — `CorrectiveRagPipeline` (`Orkeon.Rag.Corrective`), an `IRagPipeline` whose execution is a `StateGraph<RagGraphState>` (immutable record state) with conditional edges and controlled cycles: `retrieve` → `evaluate` (`IRetrievalEvaluator` → `RetrievalVerdict` `Correct | Incorrect | Ambiguous`) → per verdict `generate` / `refine` (decompose-then-recompose, never empties the working set) / `rewrite_query` (vocabulary-gap rewrite, loops back to `retrieve`) → `generate` (original question, same `[n]` rank-based markers, token budget and `edges` layout as the staged pipeline) → `check_groundedness` (`IGroundednessChecker`; ungrounded → re-loop). Every node run is traced as `corrective:<node>` with the iteration ordinal; verdicts, rewritten probes and loop count land in `RagTrace.Verdicts` / `QueryVariants` / `Iterations`. Evaluator and checker are LLM-backed when an `IChatClient` is registered (`LlmRetrievalEvaluator` / `LlmGroundednessChecker`, constrained via `LlmResponseFormat` — `json_object` where wired, e.g. DeepSeek — tolerant JSON parsing elsewhere), deterministic heuristics with a warning otherwise. `AddOrkeonCorrectiveRag(configuration)` (idempotent `TryAdd`, called by `AddOrkeonRag`).
- **Double loop bound (C1)** — `Orkeon:Rag:Corrective:MaxIterations` (default 3) bounds both the rewrite cycle and the groundedness re-loop, and the graph engine's own `CircuitBreakerPolicy` is explicitly derived from that budget as a second, independent layer. Exhaustion → best-effort generation with the best available chunks, traced; a tripped breaker is caught, traced (`corrective:circuit_breaker`) and degraded — the pipeline never throws for a loop condition and can never loop forever (circuit-breaker test included).
- **Opt-in web fallback + anti-injection (C1 security, 6C)** — after rewriting is exhausted the graph may fire `web_fallback`, gated by **two** separate off-by-default switches: `Orkeon:Rag:Corrective:WebFallback` (pipeline policy, Abstractions) and `Orkeon:Rag:WebFallback` (transport — `WebSearchDocumentRetriever`, SearxNG-compatible JSON search, `ApiKeyEnvVar` only, `AddOrkeonRagWebFallback`). Every downloaded page passes `PromptInjectionDocumentValidator` (deterministic heuristics — model-addressed directives EN+FR, chat-template control tokens, hidden HTML, exfiltration vectors; verdict `Clean | Suspicious | Rejected`): `Rejected` never leaves the retriever, `Suspicious` is flagged or discarded per `SuspiciousAction`, content is never rewritten. Threat model + honest limits (pattern-based, evadable) in `docs/architecture/security.md`. Web chunks carry `ScoreOrigin = "web"`.
- **`corrective` profile + Adaptive lift (C2, 6D)** — `RagProfile.Corrective` / `corrective` in `RagProfilePresets` and the resolver (same `IRagPipeline` façade; the profile selects the executor). Preset: hybrid BM25 + RRF (rewritten probes need the lexical leg), **no linear rerank stage** (the graph corrects by looping — no ONNX package needed), `Groundedness.Enabled = false` on purpose (the graph runs its native `check_groundedness` node whenever a checker is registered). The `adaptive` profile's `Iterative` route now delegates to the memoized `corrective` pipeline — the RAG-05 documented fallback to `quality` is **lifted** (`route` step traces `delegate=corrective`).
- **Vitrine (C3, 6B)** — `docs/architecture/rag-pipeline.md` completed (CRAG topology, verdicts, double bound, web fallback, ADR pointers); three new runnable offline examples `examples/rag/hybrid-retrieval` (BM25+RRF vs vector-only), `examples/rag/custom-reranker` (host `IReranker` via `IRerankerRegistrar`), `examples/rag/crew-yaml` (crew `rag:`/`knowledge:` blocks) + scripting variant `examples/scripting/08-rag.ork.ts`; all four `examples/rag/*` projects in `Orkeon.Examples.sln`; `examples/INDEX.md` regenerated.
- **Final evaluation, published honestly (C3)** — `orkeon rag eval --compare fast,balanced,quality,corrective,adaptive` (2026-07-26, offline): fast/balanced/quality/adaptive at 0.89 recall@5 / 0.89 MRR; **`corrective` at 0.78 / 0.64 — worse than `quality` offline, and documented as such**: the extractive stub degrades the graph's LLM nodes (pseudo-random verdicts from the tolerant parser, degenerate rewrite probe shared by all cases), so the offline row measures the loop's guard rails, not rewrite quality — full per-case analysis in `examples/rag/eval/README.md`. The causal end-to-end proof of the mechanism (verdict `Incorrect` → rewrite → `notes-power.md` cited on the seeded q-007 case that `quality` misses, same store and embeddings) is `CorrectiveRagMechanismSlowTests` (scripted LLM for the two roles CI cannot provide, labelled). CI gate unchanged (`balanced`, `correctif` cases excluded, gated aggregates 1.00/1.00).

### Added — RAG query translation & adaptive routing (RAG-05): transformers, MMR, classifier, Adaptive profile

Stage 1 of the pipeline (query transformation, guide §6) plus Adaptive-RAG routing (guide §8.4), measured with the RAG-04 harness.

- **Query transformers (C1)** — `IQueryTransformer { Name, Kind, TransformAsync }` with retrieval semantics per `QueryTransformKind`: `MultiQueryTransformer` (`multi-query`, **Union** — one LLM call produces N phrasings, retrieval runs per query, rankings merged by chunk-id union keeping the original store scores, max on duplicates), `RagFusionTransformer` (`rag-fusion`, **Fusion** — same variants, per-query rankings fused by Reciprocal Rank Fusion, same k as the hybrid stage), `HydeTransformer` (`hyde`, **Replacement** — a hypothetical document is embedded as the retrieval probe INSTEAD of the question; generation and citations always use the ORIGINAL user question), `IdentityQueryTransformer` (`none`). Factory pre-populated via `AddOrkeonQueryTransforms()` (chat client resolved lazily; unknown names fail loudly); options `Orkeon:Rag:QueryTransform` (`Mode` default `none`, `VariantCount` default 3). Unusable/failing LLM responses degrade to `[original]` with a warning — never an exception.
- **Pipeline integration (5D)** — the `StagedRagPipeline` transform stage resolves the configured transformer and wires retrieve/fuse per `Kind` (union / rrf / replacement probe). The transform step traces `transformer`, `kind`, `variants` (count) and the truncated `variant_n` texts; `RagTrace.QueryVariants` carries the full produced texts (original excluded); the fuse step traces its `method` (`union` / `rrf` / `dedup`).
- **MMR diversification (C2, opt-in)** — `MaximalMarginalRelevance.Select` applied after fusion/dedup and before rerank when `Orkeon:Rag:Retrieval:Mmr:Enabled` is set (`Lambda` default 0.7): re-orders the fused candidates by `λ·relevance − (1−λ)·redundancy`. Candidate embeddings are **not** recomputed — the documented lexical (Jaccard) fallback with min-max-normalised scores is the pipeline path; the embedding overload exists for callers that already hold them. Traced on the fuse step (`mmr`, `mmr_lambda`, `mmr_in`/`mmr_out`).
- **Query-complexity classifier (C3)** — `IQueryComplexityClassifier.ClassifyAsync(query) → QueryRoute { NoRetrieval, SingleShot, Iterative }`: `HeuristicQueryComplexityClassifier` (default — deterministic rules, zero LLM) and `LlmQueryComplexityClassifier` (constrained JSON, tolerant parsing, SingleShot fallback with warning). `AddOrkeonQueryRouting(configuration)`, options `Orkeon:Rag:QueryRouting:Classifier` (`heuristic` | `llm`; `llm` without a chat client falls back to heuristic with a warning).
- **`Adaptive` profile (C3/5D)** — `RagProfile.Adaptive` / `adaptive` in `RagProfilePresets` and the resolver: the `AdaptiveRagPipeline` classifies first, then `NoRetrieval` → direct LLM answer (no retrieval, empty citations), `SingleShot` → delegates to the memoized **balanced** pipeline, `Iterative` → **documented fallback to `quality`** until the corrective/iterative engine ships with RAG-06. The decision is always traced: `RagTrace.Route` + a first `route` step (`route`, `classifier`, `delegate`, fallback detail). `Orkeon:Rag:Profile=adaptive` routes the default pipeline through the resolver.
- **Measured comparison** (golden dataset, offline, heuristic judge, 2026-07-26): fast / balanced / quality / adaptive all at 0.89 recall@5 / 0.89 MRR (4 / 217 / 166 / 141 ms/case). **Adaptive equals balanced on this dataset by construction**: the heuristic classifier routes all 9 golden questions to SingleShot → balanced (each has exactly one interrogative word, a single `?`, and fewer than 25 words); the ms/case delta is a warm-ONNX-session artifact of run order, not a quality gain. Offline honesty: `--offline` swaps in a deterministic extractive chat client, so the LLM-backed transformers (multi-query / rag-fusion / hyde) and the `llm` classifier have no real LLM to call — the measured path is `Mode=none` + heuristic routing; the transformer/MMR levers are wired and unit-tested, their quality delta will be measured with a real `IChatClient` and/or the RAG-06 corpus growth.

### Added — RAG quality phase (RAG-04): evaluation harness, hybrid retrieval, reranking, profiles

Measured before proclaimed — the whole phase is driven by an offline CI-runnable evaluation harness (local BGE embeddings, embedded ONNX cross-encoder, deterministic extractive generation, labelled judge — zero network, zero API key).

- **Evaluation harness (C1, plan §9)** — versioned golden dataset `examples/rag/eval/golden.yaml` (7 cases over a 10-document corpus, incl. the seeded hard-retrieval case `q-007` tagged `correctif`), deterministic retrieval metrics (recall@k, precision@k, MRR), generation metrics via LLM-judge with deterministic heuristic fallback (the mode used is **always labelled**), `IRagEvaluator`/`IRagEvalHarness`, agent tool `rag_eval`, CLI `orkeon rag eval --dataset … [--profile|--compare] [--offline] [--min-recall --min-mrr]`, dedicated workflow `.github/workflows/rag-eval.yml`.
- **Hybrid retrieval (C2, plan §5)** — in-process `Bm25Index` + `ReciprocalRankFusion` (RRF k=60) behind the `HybridSearchDocumentStore` decorator (works over all 6 memory providers; in-process index memory documented), native provider hybrid preferred via the new `IHybridSearchCapable` Domain capability (LanceDB), native scores via `IScoredVectorSearch` (ChromaDB/Pinecone/LanceDB).
- **Reranking (C3, plan §7)** — opt-in packages `Orkeon.Rag.Onnx` (cross-encoder runtime, ms-marco-MiniLM-L-6-v2, Apache-2.0) + `Orkeon.Rag.Onnx.Model` (int8 weights **embedded** — guaranteed offline, no download ever) registered with `AddOrkeonOnnxReranker()`; `LlmListwiseReranker` (universal fallback over the 12 providers) and `NoopReranker`; default cascade CandidateK 50 → TopN 5.
- **Staged pipeline + profiles (C4, plan §5.1–5.2)** — `StagedRagPipeline` replaces `LinearRagPipeline` (breaking rename, beta window): transform (hook `none`, RAG-05) → retrieve (CandidateK, per-query hybrid) → fuse (RRF/dedup) → rerank (named, `RerankerFactory`) → assemble (token budget + **anti-Lost-in-the-Middle `edges` ordering**: ranks 1, 3, 5… open the context, …6, 4, 2 close it — best two chunks at the extremities, rank-stable `[n]` markers) → cited generation → optional groundedness hook (checker ships with RAG-06); every stage traced in `RagAnswer.Trace`. `RagProfile { Fast, Balanced, Quality }` + `RagProfilePresets` → **`RagOptions` v2** bound on `Orkeon:Rag` (profile = preset, configuration = per-key override; defaults in `Orkeon.Domain.Constants.Rag.RagDefaults`); `ProfileRagPipelineResolver` builds and memoizes one pipeline per profile (unknown names fail loudly listing `fast, balanced, quality, default`). Default profile is `fast` (deviation from plan §5.2's `balanced`: balanced requires the opt-in ONNX package; opt in with one key `Orkeon:Rag:Profile=balanced`). The store is now always wrapped in the hybrid decorator (ingestion feeds BM25; disabled default mode = strict passthrough); `RetrievalQuery.Hybrid` toggles fusion per query.
- **Measured comparison** (golden dataset, offline, heuristic judge, 2026-07-26): fast 0.89 recall@5 / 0.89 MRR / 3 ms/case; balanced 0.89 / 0.89 / 139 ms/case; quality 0.89 / 0.89 / 105 ms/case (9 cases, incl. two exact-identifier lookups q-008/q-009 that vector-only also resolves at this corpus scale). The three profiles tie **on this dataset by construction**: the eight regular cases are saturated by plain vector retrieval (1.00/1.00 each, `correctif` excluded ⇒ gated aggregates 1.00/1.00 for all profiles), and the seeded `q-007` defeats the cross-encoder too (measured score of the relevant `notes-power.md`: 0.0000, dead last; decoy `faq-battery.md`: 0.9997) — vocabulary bridging is exactly the RAG-05/RAG-06 lever (plan §9.1: `correctif` must fail until Corrective). The CI gate runs on the **balanced** profile and the fast/balanced/quality table is published in the workflow step summary.

### Changed — **BREAKING: RAG subsystem extraction (RAG-02, no shims)**

The RAG feature set is promoted to a first-rank subsystem (`src/rag/` — `Orkeon.Rag.Abstractions` contracts + `Orkeon.Rag` implementations, agent tools in `src/tools/Orkeon.Tools.Rag`; see [ADR-006](docs/adr/ADR-006-rag-subsystem.md)). The legacy namespaces `Orkeon.Application.Interfaces.Rag.*`, `Orkeon.Application.Rag.*`, `Orkeon.Application.Interfaces.Knowledge.*` and `Orkeon.Infrastructure.Knowledge.*` are **removed without `[Obsolete]` shims** (assumed break, decision 2026-07-25, `0.9.x-beta` window).

Opt-in wiring: `services.AddOrkeonRag(configuration)` (namespace `Orkeon.Rag.DependencyInjection`, self-sufficient `TryAdd*`, default `IDocumentStore` = `MemoryProviderDocumentStore` over the ambient `IMemoryProvider`) + `services.AddOrkeonRagTools()` (`Orkeon.Tools.Rag.DependencyInjection`, registers `rag_search`). Neither is called by `AddOrkeonInfrastructure()`.

Migration table (old type → new type):

| Old (removed) | New |
|---|---|
| `Orkeon.Infrastructure.Knowledge.RagTool` (`rag_search`) | `Orkeon.Tools.Rag.RagSearchTool` (`rag_search` — same name, schema `question`/`top_k`/`collection`, and output format `answer` + `Sources:` block; `collection = "raggable-tree"` still routes to `IRaggableStore`) |
| `AddOrkeonRag` (Infrastructure `RagServiceExtensions`) + `AddOrkeonKnowledge` + `AddOrkeonRagValidation` | `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection.RagServiceCollectionExtensions` — loaders + ingestion validation + pipelines + factories) + `AddOrkeonRagTools()` |
| `Orkeon.Application.Interfaces.Rag.IRagPipeline` (`ExecuteAsync(question, RagOptions)` → `RagResult`) | `Orkeon.Rag.Abstractions.Interfaces.IRagPipeline` (`QueryAsync(RagQuery)` → `RagAnswer` with citations + trace) |
| `Orkeon.Application.Rag.RagPipeline` + `ChatClientResponseGenerator` | `Orkeon.Rag.Pipeline.StagedRagPipeline` (named `LinearRagPipeline` until RAG-04/C4) |
| `KnowledgeService` (ingestion side) | `Orkeon.Rag.Pipeline.DefaultIngestionPipeline` (`IIngestionPipeline`) |
| `IKnowledgeService` (Application port) | `Orkeon.Rag.Abstractions.Interfaces.IDocumentStore` (storage/search) + `IIngestionPipeline` (ingestion) + `IRagPipeline` (query) |
| `TextFileLoader` / `CsvDocumentLoader` / `HtmlDocumentLoader` / `PdfDocumentLoader` / `DocumentLoaderFactory` (`Infrastructure.Knowledge.Loaders`) | `Orkeon.Rag.Loaders.*` (same names, `IDocumentLoader` over `SourceDescriptor` → `RagDocument`) |
| `WebPageLoader` (`Infrastructure.Knowledge.Loaders`) | `Orkeon.Rag.Loaders.WebPageLoader` (typed `HttpClient`, kinds `url`/`web`) |
| `RecursiveTextChunker` / `SentenceChunker` (+ tool-local chunker copies) | `Orkeon.Rag.Chunking.*` — `IChunkingStrategy` implementations `recursive`, `sentence`, `structural`, `semantic` |
| `ContentIntegrityValidator` / `PromptInjectionDocumentValidator` / `DataValidationPipeline` / `ProvenanceTracker` / `IQuarantineStore` + `InMemoryQuarantineStore` (`Infrastructure.Knowledge.Validation`) | `Orkeon.Rag.Validation.*` (same names — validation stays on the ingestion path) |
| `AnalysisEmbeddingProviderAdapter` (`Infrastructure.LLMs.Embeddings`) | `Orkeon.Rag.Embeddings.AnalysisEmbeddingProviderAdapter` |
| `SimpleEmbeddingService` (hash-based, `[Obsolete]`) | **Removed without replacement.** The default `IEmbeddingService` now adapts the `IEmbeddingProvider` port (`EmbeddingProviderServiceAdapter`): local BGE → remote `Orkeon:Embeddings` → fail-fast at first use. Semantic agent selection inherits the real chain. |
| `HybridScorer` (`Infrastructure.Knowledge.Retrieval`, dead code) | **Removed without replacement** (hybrid search capability lives in `Orkeon.Domain.Memory.IHybridSearchCapable`) |
| `FileKnowledgeSource` / `DirectoryKnowledgeSource` / `WebKnowledgeSource` / `DatabaseKnowledgeSource` (`Infrastructure.Knowledge.Sources`) | **Removed without replacement** — describe sources with `SourceDescriptor` and run them through `IIngestionPipeline` (`IngestionRequest`). The Domain contract `Orkeon.Domain.Knowledge.IKnowledgeSource` remains (no framework implementations). |
| `RagOptions` / `RagPipelineOptions` / `RagDefaults` / `KnowledgeContext` / `KnowledgeItem` / `RagTypes` (`RagResult`, `RetrievalOptions`…) | `Orkeon.Rag.Abstractions.Models.*` (`RagQuery`, `RagAnswer`, `Citation`, `ScoredChunk`, `RetrievalQuery`…) + `Orkeon.Rag.Abstractions.Options.RagOptions` v2 (section `Orkeon:Rag`, RAG-04/C4) / `RagIngestionOptions` (section `Orkeon:Rag:Ingestion`); defaults in `Orkeon.Domain.Constants.Rag.RagDefaults` |
| `ResearchFindings` (was in `Orkeon.Application.Rag`) | **Kept** (not RAG) — moved to `Orkeon.Application.Services.Generic` |

## [0.9.2-beta] - 2026-07-24

First version actually published to GitHub Packages since `0.9.1-beta` (2026-07-04): the intermediate `v0.9.1-beta.rc*` tags re-packed the unchanged `0.9.1-beta` version from `Directory.Build.props`, so `--skip-duplicate` silently skipped every push. This release bumps the props version so the feed picks up everything below.

### Added

- **`IFileSystemScope`** (`Orkeon.Domain.FileSystem`) — ambient per-scope mount override for the VFS.
- **`ILlmDeltaSink`** (`Orkeon.Application.Interfaces.Ports`) — streaming delta sink port for LLM output.

### Security

- **A2A mTLS server now authenticates the client certificate instead of merely checking its dates** (SEC-012, R9.1). With `RequireMutualTls = true`, an incoming certificate must chain to one of `A2ASecurityOptions.TrustedCertificateAuthorities` (X509 `CustomRootTrust` chain — also covers validity dates, removing the last `DateTime.Now` in `src/`) or match the new `TrustedClientCertificateThumbprints` pin list; unpinned self-signed certificates are rejected (403). Starting the server with `RequireMutualTls` and no trust anchor now **throws** (fail-closed) — previously any date-valid certificate passed the guard. Revocation is not checked (private CAs without CRL/OCSP assumed).
- **A2A client-side CA pinning no longer bypasses host-name validation** (SEC-011, R9.1). `A2ASecurityHandlerFactory` only vouches for `RemoteCertificateChainErrors` (private CA unknown to the OS store); `RemoteCertificateNameMismatch`/`RemoteCertificateNotAvailable` are never accepted. The explicit `ValidateServerCertificate = false` opt-out now logs a security warning (local development only).
- **A2A mTLS handler is built once and cached instead of per call** (ANT-018, R9.2). On every A2A call (`send`, `sendSubscribe`, `cancel`, `status`) the client used to re-read the PFX through the VFS, re-import the `X509Certificate2` (never disposed) and create a fresh handler+client — a full mTLS handshake per call. `A2ASecurityHandlerFactory` now returns a pooled `SocketsHttpHandler` (`SslOptions.ClientCertificates`, `PooledConnectionLifetime` 2 min) cached lazily by `A2AClient`; per-call clients wrap it with `disposeHandler: false`. `A2AClient` is now `IDisposable` and disposes the handler and the imported certificate exactly once. Certificate rotation requires a new client instance (options snapshot at construction).

### Changed

- **Public API reshaped to .NET design-guideline conformance; 8 API-shape rules frozen as build errors** (R11.7 / maintainer decision D1 = "fix everything", **breaking, 0.9.0-beta**). The API-shape analyzer family was driven to zero across all `src/` with no `severity = none` carve-out:
  - **Exposed collections are read-only** (CA1002/CA2227/CA1819): `List<T>`/`T[]` properties and returns become `IReadOnlyList<T>`; settable collection properties become `init`/get-only. Deserialization-safe by construction — `IConfiguration`-bound options use `Collection<T>` get-only, YamlDotNet DTOs keep a settable `Collection<T>?`, System.Text.Json DTOs use `IReadOnlyList<T>` init; graph/builder state keeps a private mutable backing field exposed read-only.
  - **URLs are `System.Uri`** (CA1054/CA1056): string URL parameters and properties across `IHttpClient`, `IA2AClient`, `LlmConfig.BaseUrl`, options and tool DTOs now use `System.Uri`.
  - **Cross-language-safe naming** (CA1716): interface/virtual parameters and members renamed off reserved keywords (`ISpecification<T>.And/Or/Not` → `AndWith/OrWith/Negate`, `IAgentRegistrationStore.Get` → `GetById`), and the **`Orkeon.Domain.Shared` namespace renamed to `Orkeon.Domain.SharedKernel`** solution-wide (it collided with the VB `Shared` keyword).
  - **Getters and nesting** (CA1024/CA1034): getter methods (`GetX()`) become properties; public nested types are un-nested (with a qualifying rename where the bare name was generic, e.g. `OrkeonDiagnostics.Tags` → `OrkeonDiagnosticTags`) or made `internal` when they are implementation details.

  All changes are behaviour-preserving (order, JSON wire format, and config/YAML binding verified empirically per layer). `dotnet build Orkeon.sln` stays at 0 warning / 0 error. Note: this is a deliberate breaking change to the public surface, taken inside the 0.9.0-beta window before the first NuGet tag; some test assertions (URL equality, null-argument exception types, read-only collections) are updated accordingly and validated on the Windows test run.
- **Redundant default-value initializers removed and `System.Random` audited across `src/`; CA1805 + CA5394 frozen as build errors** (R11.5, zero-warning campaign hygiene wave). The 89 src fields/auto-properties explicitly initialized to their default value (`= 0/false/null/default/new()`) had the redundant initializer removed (CA1805, mechanical, no behaviour change). The 3 src `System.Random` uses flagged by CA5394 are all provably non-security (a SHA256-seeded deterministic pseudo-embedding fallback, retry-backoff jitter, and a simulated research-confidence score) and now carry a tight justified `#pragma warning disable CA5394`; the rule is frozen as `error` so any new `Random` use trips the build and gets a security review. CA1822 (mark members static) was intentionally deferred — several flagged members are exposed to JS scripts via Jint instance reflection and making them static would break the scripting API. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Logging converted to the `LoggerMessage` source generator across all `src/`; CA1848 + CA1873 frozen as build errors** (R11.3, zero-warning campaign wave B3). The 113 src `ILogger.Log*` call sites flagged by CA1848 (use the LoggerMessage delegates) are now `[LoggerMessage]` source-generated partial methods (per-class EventIds, message templates and structured placeholder names preserved verbatim, exceptions passed as method arguments), and the 34 CA1873 sites (arguments evaluated even when the level is disabled) are fixed by that conversion or by hoisting the expensive expression into a local inside an `IsEnabled` guard. The single dynamic-level audit sink uses cached `LoggerMessage.Define` delegates. Both rules are locked as `error` for `src/**`. Behaviour is unchanged (same levels, templates, args, exceptions); tests and `examples/` keep their occurrences and are not frozen. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Externally-visible method parameters are null-guarded across all `src/` and CA1062 frozen as a build error** (R11.2, zero-warning campaign wave B2). Every externally-visible method that dereferenced a reference parameter without checking it for null now guards it — 605 unique src sites get `ArgumentNullException.ThrowIfNull(param)` as their first statement (the netstandard2.0 analyzer project uses the classic `if (x is null) throw` form), and CA1062 is locked as `error` for `src/**` so no public entry point can ship unguarded. Body-only edits, no signature/behaviour change: expression-bodied methods became block bodies, constructor-initializer dereferences use `(param ?? throw …)`, and the contractually-nullable null-tolerant JS-facing logging shims coalesce (`?? JsValue.Undefined`) instead of throwing (a throw there would have regressed their documented null-tolerance). Tests and `examples/` (separate solution / test doubles) keep their occurrences and are not frozen. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Globalisation analyzer family resorbed to zero across all `src/` and frozen as build errors** (R11.1, zero-warning campaign wave B1). The culture-sensitive string-operation rules CA1307/CA1310 (`StringComparison`), CA1305/CA1304 (`IFormatProvider`/`CultureInfo`), CA1311 (culture-aware case) and CA1308 (`ToLower`) are fixed at 363 unique sites and locked as `error` for `src/**` in `.editorconfig`, so framework code can never silently reintroduce one. Arbitrage "Ordinal default + protect ToLower": comparisons → `StringComparison.Ordinal` (`OrdinalIgnoreCase` for identifier/key matching), formatting → `CultureInfo.InvariantCulture`, and the 91 `ToLowerInvariant()` sites that *produce* a wire/storage/switch value keep their lowercase form under a tight justified `#pragma warning disable CA1308` (6 comparison-only sites rewritten to ordinal-ignore-case). The same six rules are neutralised (`severity = none`) in `tests/.editorconfig` — test assertions compare literal values where ordinal is already the default — a documented arbitrage, not a suppression. These rules are outside the default analysis set, so enabling them as errors enforces them in the normal build without `AnalysisMode=All`; `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Pinned Terminal.Gui to 2.0.1** (was 2.1.0). v2.1.0 shipped 2026-05-08 with a major API redesign + rendering bugs (gray-on-gray default scheme, focus on read-only widgets, missing `FakeDriver` for tests). 2.0.1 is the last stable release before that redesign. Same code compiles unchanged (API surface compatible). The xUnit `ModuleInitializer` crash (TUI-12) exists in both versions, so view-touching tests stay skipped.

### Fixed

- **Null-argument contracts reconciled with the R11.2 guards across Domain/Application/Scripting tests** (R11.2 follow-up, surfaced by the full Windows test run). Six tests and one value object that still encoded pre-guard behaviour are aligned with the `ArgumentNullException.ThrowIfNull` guards added in R11.2: `ValidationResult.Combine`, `TypedTaskContext.Transform`, `AgentMapper.ToDto`/`CreateFromRequest` and `SequentialCrewOrchestrator.KickoffAsync` now correctly reject null (the orchestrator's `CrewInput` has no empty form, so a null input is genuinely invalid — the test that expected "graceful" handling now expects the throw). `TaskDescription.From` is the one behavioural fix: the R11.2 `ThrowIfNull` had fragmented its validation so a null value surfaced `ArgumentNullException` ("Value cannot be null") instead of the value object's unified `ArgumentException` ("… cannot be null or whitespace") used for empty/whitespace — it now validates null/empty/whitespace uniformly in one guard (still CA1062-clean). A telemetry test (`OtelTests.tool_call_emits_a_span_with_tool_name_tag`) was also made robust against the process-global `ActivityListener` picking up a concurrent test's tool-call span, by filtering on the unique `tool.name` tag like the sibling crew/agent span tests already do. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Tool URL parameters typed as `System.Uri` no longer break the agent tool contract** (R11.7-D1 follow-up). The D1 API-shape wave (CA1054/CA1056) converted tool request/response URL properties to `System.Uri`, but the typed-tool schema generator mapped `Uri` to JSON type `"object"` — so every tool call carrying a string URL was rejected before execution with `Parameter 'url' has invalid type. Expected: object` (≈30 direct failures cascading across `web_scrape`, `scrape_element`, `http_api`, `cache_search` and `arcadedb_query`). Fixed at a single point rather than reverting the DTOs: `ToolSchemaGenerator` now maps `Uri` to `"string"` (format `uri`) like `Guid`/`DateTime`, and a new tolerant `UriTolerantConverter` (registered only in the component pipeline's options) round-trips `Uri`↔string — empty/whitespace maps to `null` (so a tool's own "URL cannot be empty" validation reports a friendly error instead of an opaque JSON failure), relative values are accepted (`UriKind.RelativeOrAbsolute`, e.g. a host-substring filter), and writes use `OriginalString` to preserve the exact URL with no trailing-slash canonicalisation. The `Uri` typing (and the frozen CA1054/CA1056 errors) are kept. The same `string`→`Uri?` conversion had also silently flipped the required `url` parameter of `web_scrape`/`scrape_element`/`http_api` to optional in the generated schema (a non-nullable `string` infers required; a nullable `Uri?` infers optional); those three inputs are marked `[FieldSchema(IsRequired = true)]` to restore the pre-D1 schema while keeping the property nullable for the tool's own emptiness check (`cache_search`'s URL substring filter stays optional; `arcadedb_query`'s `bolt_uri` was already required). Also corrected three test concerns surfaced by the same Windows run: the `MockHttpMessageHandler`/`TestHttpMessageHandler` doubles now capture a **buffered clone** of each request so post-send body assertions survive the provider's R10.2 content disposal (was `ObjectDisposedException` ×27), the `IUrlValidator` SSRF stub records `OriginalString` instead of the canonicalised form, and three `LlmBasedManager` null-argument tests now expect the `ArgumentNullException` the R11.2 guards correctly throw (was `NullReferenceException`). `dotnet build Orkeon.sln` stays at 0 warning / 0 error; the test suite is re-run on Windows.
- **Conversation roles are matched case-insensitively everywhere** (SML-009, R12.5). The HTTP providers compared `msg.Role == "assistant"` (case-sensitive) while `ConversationPolicy` used `OrdinalIgnoreCase`, so a mixed-case role like `"Assistant"` was serialized one way and policy-matched another. A new `LlmRoles` (canonical lowercase wire values + a single `Is`/`IsX` helper) now routes every comparison and message construction in the Anthropic/OpenAI-compatible providers, `ConversationPolicy`, and the `LlmMessage` factories. This fixes a real bug where a mixed-case `"Tool"` orphan survived history trimming and produced the orphan `tool_result` the trim exists to prevent. The scripting default models (`LlmNamespaceBinding`) were de-duplicated (they were defined twice in the same file); reconciling them with `ProviderDefaults` is a maintainer product decision (the values differ).
- **`release.yml` and `ci.yml` no longer publish divergent NuGet perimeters** (OSS-011, R8.3). `release.yml` was missing `Orkeon.Infrastructure` even though the README documents installing it; both workflows now pack the same three core libraries. The published set is documented in `docs/reference/publication-matrix.md` (promotion of the tools family / `orkeon` tool is held until decision D3 so the scripting twins' names are not locked into NuGet before a possible rename). Stale metadata fixed: the `CONTRIBUTING` clone/upstream URLs (`Orkeon/orkeon`), the `Orkeon.ConsoleApp` local `1.0.0` version override (now inherits `0.9.0-beta`), and the obsolete `+orkeon` coverage-filter comment.
- **`HttpRequestMessage`/`HttpResponseMessage` are disposed on the LLM request path** (ANT-006, R10.2). The 20 per-call CA2000 leaks in the Anthropic/OpenAI-compatible/Ollama providers and `HttpClientAdapter` are fixed by real lifetime: `using var` for non-streaming request/response, dispose-after-send for the streaming request (the response escapes but the request body is already transmitted), and the previously-leaked cloned retry request in `ExecuteHttpRequestAsync`. The 11 `LlmProviderFactory` sites are ownership transfers (the provider is wrapped in the returned adapter; its `Dispose` is a no-op) — collapsed into one generic `Adapt<T>` helper carrying a single justified suppression.
- **Semantic memory recall no longer silently returns empty on the default configuration** (MAT-017, R10.1). `IMemoryProvider.SearchSimilarAsync` was a default interface method returning empty, and `InMemoryProvider`'s real cosine implementation was unreachable through the interface (C# does not re-map derived members onto a base-implemented interface). `SearchSimilarAsync` is now an **abstract member of `MemoryProviderBase`** (compiler-enforced for every provider), the four affected providers re-list the interface, and Redis/ChromaDB/Pinecone — which had **no** implementation at all — gained real vector searches (server-side query for Chroma/Pinecone, client-side cosine for Redis). A reflection test locks the interface map for future providers.
- **`ICodeSandbox` resolution no longer launches a `docker version` process under the DI singleton lock** (ORG-012, R10.3). The availability probe lives in a memoized lazy decorator (`LazyProbingCodeSandbox`) and runs at the first `ExecuteAsync` (async, cancellable); the probe process is now killed on timeout/cancellation. Fail-closed semantics of the sandbox gate are preserved byte-for-byte. An architecture test bans `GetAwaiter().GetResult()`/`.Result` in `src/` DI factories (single documented exemption: plugins).
- **Script command loading no longer blocks the DI thread** (ANT-002, R10.3). `ScriptCommandRegistry` resolution is pure wiring; discovery + esbuild transpilation + Jint evaluation are deferred to a memoized task awaited at runner startup (failures are not memoized — next call retries).
- **Script `ctx.services.get("tools")` no longer materializes a new set of transient disposable tools per call** (ANT-005, R10.4). The whitelist resolves the tool set once (thread-safe lazy) and serves a fresh shallow copy of the same instances — unbounded memory growth in long REPL sessions is gone.
- **`MemoryProviderFactory` no longer creates bare `HttpClient`s** (ANT-013, R10.5). Chroma/Pinecone/LanceDB clients use `SocketsHttpHandler` with `PooledConnectionLifetime` (2 min) so rotating cloud endpoints are re-resolved; ChromaDB and Pinecone providers gained the missing `Dispose` (the client was never released) and all three providers are now `IDisposable`.
- **`CrewOutput.TokensUsed` is real telemetry for all 6 process types** (MAT-004, R10.8). Parallel/Hierarchical/Consensual/Autonomous now record token usage (new thread-safe `TokenUsageTally`), Graph propagates its existing internal count (success and circuit-breaker paths), and the prompt/completion split is extracted from `UsageDetails` instead of being discarded. **Breaking (0.9.0-beta)**: `TokensUsed` is now nullable — `null` means "not measured", never a fabricated `TokenUsage(0,0,0)`; the persisted checkpoint projection records `TokensMeasured` so the distinction survives round-trips.
- **Application placeholders implemented or removed** (MAT-018, R10.9). `CrewConfigurationMapper.ToConfiguration` exports agents and tasks for real (round-trip tested; **breaking**: the caller now provides the materialized entities); `CrewValidator` validates LLM configs (model + canonical numeric bounds); `CrewPlanner` consumes its previously-ignored `strategy` parameter; the stub `AgentPlannerService` is explicitly documented and logs at use. **Removed**: `IMemoryCoordinator.ClearTemporaryMemoriesAsync` (no production caller, no "temporary" marker in the model — the no-op could not be made honest).
- **`AddOrkeonA2A()`/`AddOrkeonInfrastructure()` call order no longer matters for the A2A agent directory** (ANT-019, R9.3). `IAgentRepository` is now registered with `TryAddScoped` by the infrastructure: when A2A ran first, its `SharedStoreAgentRepository` upgrade used to be silently won back by the later `AddScoped` (per-scope empty directory — ANT-001's failure mode, no crash). Side effect: a host repository registered **before** the Orkeon extensions is no longer shadowed by the in-memory default; hosts that override `IAgentRepository` **after** `AddOrkeonInfrastructure()` without `Replace` keep the last-wins behaviour as before.
- **Ctrl+C in TUI cancels the current command instead of being intercepted as SIGINT** (TUI-20) — `Console.TreatControlCAsInput = true` is set after `Application.Init` so Ctrl+C reaches Terminal.Gui's input loop. The .NET runtime's `Console.CancelKeyPress` hook is skipped in TUI mode (would race with the keystroke path). A global `Application.KeyDown` handler in `TerminalGuiHost` is the source of truth: 1×Ctrl+C requests `IInteractiveRunner.RequestCommandCancellation` (with REPL-pane feedback `⏹  Cancellation requested...`); 2×Ctrl+C within 2s force-quits the TUI as an escape hatch. `RunOneShotAsync` now accepts an `externalCt` parameter so `VerifyCommand` propagates the per-command CT into the inner crew kickoff.
- **Ctrl+Q during a running command shows a confirmation dialog** — uses `MessageBox.Query` (Terminal.Gui's idiomatic modal). The previous custom `QuitConfirmDialog` exposed a v2 runnable-stack race that swallowed the post-dialog `Application.RequestStop`.
- **Inner-host logs (RunOneShotAsync) leaked to stdout in TUI mode** (TUI-19) — when `verify` spawned a child host with `AddSimpleConsole`, those writes hit `System.Console.Out` (commandeered by Terminal.Gui's alt-screen) and dumped on shutdown. New `Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider` is published by the TUI host on Initialize; `RunnerExecution.ConfigureVerboseLogging` resolves it via reflection (no project coupling) and substitutes it for `AddSimpleConsole` when present. Inner host logs now flow into the logs pane.
- **Default `LoggerFactory` minimum level too restrictive in TUI** — runner now sets `LogLevel.Trace` upstream so all entries reach `TerminalGuiLoggerProvider`; visible filtering is owned by the provider (toggled at runtime via F2 / Shift+F2 in the status bar).
- **Gray-on-gray invisible text + missing show/hide logs** — Terminal.Gui default scheme paints fg and bg in the same gray, so panes appear empty until you select with the mouse. New `Orkeon.Cli.TerminalGui.Layout.SchemeFactory` builds explicit white-on-black schemes wired into LogsPaneView/ReplPaneView/SplitPaneToplevel. `Ctrl+G` now toggles the logs pane visibility (REPL fills the screen when hidden).
- **Initial focus landed on the read-only logs pane instead of the prompt input** (TUI-16) — `_history.CanFocus = false` removes it from the focus cycle, plus an `Initialized` hook on `ReplPaneView` calls `_input.SetFocus()` once the view is laid out so typing works immediately without clicking.
- **Status bar shortcuts swallowed by focused TextField** (TUI-17) — every Shortcut now sets `BindKeyToApplication = true` so keystrokes route at app level regardless of focus.
- **Ctrl+Q hung the host when the runner was blocked in `Console.ReadLine`** (TUI-17) — sync-over-async wrapper passed `CancellationToken.None`. `ReplPaneView.CancelPendingRead()` is invoked from the host's finally block to forcibly complete the pending TCS with `null` so the runner's loop sees `ReadLine() == null` and exits.
- **Hosts hung when the runner ignored cancellation during a long command** — added a 2s grace period after `linkedCts.Cancel()`; if the REPL task doesn't honour cancellation within the window, it's abandoned (orphaned task finishes in background, host exits cleanly).
- **Terminal.Gui split-pane silently exited at startup** (TUI-14) — `TerminalGuiHost(options, loggerProvider)` formed a cycle with the `TerminalGuiLoggerProvider` DI factory; `.Host.Build()` exited with code 0 before any UI was rendered. `TerminalGuiHost` now takes only `TerminalGuiOptions`; the status bar is built and attached from the `TerminalGuiLoggerProvider` factory once both exist. Also: explicit `DOTNET` driver on Linux/macOS (TUI-13), since the default driver emits no output in WSL.

### Added

- **Bilingual documentation parity gate** (OSS-012, R8.4). `scripts/check-docs-parity.sh` fails CI when any `docs/**.md` lacks its `docs/fr/` mirror (or vice versa), or when a root `README`/`CONTRIBUTING`/`CODE_OF_CONDUCT`/`SECURITY` `.md` lacks its `.fr.md` pair; wired as the `docs-parity` job. The rule is documented in both `CONTRIBUTING` files.
- **Blocking npm vulnerability audit** (DEP-010, R8.6). `dependency-audit.yml` gains an `npm-vulnerability-audit` job (`npm ci --dry-run` integrity + `npm audit --audit-level=high` over the esbuild bootstrap), so a CVE on `esbuild`/`@esbuild/*` fails CI instead of being an ignorable Dependabot PR. NuGet lockfile pinning was considered and deferred (bump friction outweighs the reproducibility gain already covered by Dependabot + the blocking audit).
- **Native tool calling for Azure OpenAI** (FON-011, R10.7). `AzureOpenAILlmProvider` is rebased on `OpenAICompatibleProviderBase` (its hand-rolled pipeline only read `choices[0].message.content`) and the factory passes the OpenAI strategy: `tools`/`tool_choice` are injected and `tool_calls` parsed natively, with the text fallback kept as the safety net. Ollama stays on the text protocol — its `/api/generate` pipeline is prompt completion and the native-tools `/api/chat` response format is incompatible with the OpenAI parser; documented in `docs/reference/limitations.md`.
- **`runCrew` script calls are bounded by a configurable timeout** (ANT-007/ANT-010, R10.10). Default 10 minutes (`ScriptHostFacadeOptions.RunCrewTimeout`, ≤ 0 disables): an infinite crew no longer freezes the REPL; scripts get a clear `TimeoutException`. `IConsoleAdapter` gains `ReadLineAsync`/`ReadKeyAsync` as non-breaking default interface methods, with real TUI overrides on the existing TCS machinery. The assumed-blocking design (Jint is synchronous) is documented in the scripting guide (EN+FR).
- **Terminal.Gui split-pane console (`Orkeon.Cli.TerminalGui`)** — new `IConsoleAdapter` + `ILoggerProvider` that route REPL I/O and `ILogger` writes into separate panes (logs on top, REPL on bottom), driven by Terminal.Gui v2.0.1. All 4 interactive runners (`ClaimVerifierRunner`, `MainMenuRunner`, `QaRunner`, plus the new `--ui` flag in `Orkeon.ConsoleApp` + `examples/runners/interactive-claim-verification`) accept `--ui tui|plain|auto`, default `auto` (TUI when interactive TTY, plain in CI/pipe via `TtyDetector`). Wire-up: `services.AddOrkeonCliTerminalGui()`. Full keybindings: Ctrl+L (clear logs), Ctrl+K (clear REPL), Ctrl+F (find), Ctrl+G (toggle logs pane), Ctrl+↑/↓ (resize split), F2 (more log details) / Shift+F2 (less log details), Ctrl+C (cancel current command — 2× to force-quit), Ctrl+Q (quit, with confirmation dialog if a command is running). TUI-18 tracks the Terminal.Gui 2.1.x re-evaluation follow-up.
- **`IInteractiveRunner` interface** in `Orkeon.Cli.Abstractions.Runners` exposes `IsCommandRunning` + `RequestCommandCancellation()`. `InteractiveRunnerBase` implements it; the TUI uses it to drive Ctrl+C cancellation and Ctrl+Q confirmation dialogs.
- **`AmbientLoggerProvider`** in `Orkeon.Cli.Abstractions.Logging` — process-wide ambient `ILoggerProvider` registry letting child hosts re-route their logs into a parent TUI's pane without project coupling (resolved via reflection by `RunnerExecution.ConfigureVerboseLogging`). Wrapped in a non-owning `LeasedLoggerProvider` so child host disposal doesn't kill the parent's provider.
- **TUI key event diagnostic** (`examples/runners/tui-keytest`) — small standalone runner that boots Terminal.Gui and logs every keystroke arriving at `Application.KeyDown` to `/tmp/tui-keytest.log`. Used to diagnose terminal-specific keystroke routing issues (TUI-20). Run with `dotnet run --project examples/runners/tui-keytest`.
- **Virtual FileSystem v2.2** — enumeration + streaming surface on `IFileSystemService` (`EnumerateFilesAsync`, `OpenReadStreamAsync`, `TryReadAllBytesAsync`, `TryReadAllTextAsync`, `GetEntryKindAsync`). `FileSystemDiscoverer` now goes through the VFS instead of raw `System.IO`.
- **Virtual paths across the RaggableTree pipeline** — `RaggableNode.FilePath` → `VirtualFilePath`, propagated through adapters, tools, DTOs (`SourceSlice`, `SymbolSourceResponse`), serializer (bumped to v2.0), and the store. Builder reads source via `IFileSystemService.TryReadAllTextAsync`.
- **Index introspection tools** — `index_status` and `is_path_indexed` let agents check which virtual roots are indexed and whether a given virtual path is covered (longest-match on overlapping roots). `IRaggableStore.GetIndexedRoots()` exposes the underlying list.

### Removed / Breaking

- **`ImprovedAgentExecutionService` renamed to `AgentExecutionService`** (SML-008, R12.4) — it is the only implementation of `IAgentExecutionService`, so the "Improved" qualifier was meaningless. The value object `Version` (which shadowed `System.Version`) is renamed `SemanticVersion`. Internal/pre-freeze renames in the 0.9.0-beta window. The dead `JsonToolCallParser` (`[Obsolete]`, no usage, no DI registration) and the empty `JsAgentInstance` placeholder are removed.
- **`--prebuild-index` CLI flag** removed from the standard runner. Agents now call `index_codebase(root_path="/src")` themselves (optionally gated by `is_path_indexed`). The flag pre-built an index the agent could not scope; letting the agent index what it needs, when it needs it, removed a whole-tree cost from every run.
- **Serialization format bump** — RaggableTree on-disk cache goes from v1.0 to v2.0 (field rename `FilePath` → `VirtualFilePath`). Existing caches will fail to load and need to be rebuilt.

## [0.9.1-beta] - 2026-07-04

Published to GitHub Packages only, without a dedicated changelog section at the
time; its changes are folded into the [0.9.2-beta] entries above. Recorded here
so the version chain has no gap. The `v0.9.1-beta.rc*` tags that followed
re-packed this unchanged version, so `--skip-duplicate` silently skipped every
push — the incident that motivated the tag↔version guard in the publish
workflow (see [0.9.2-beta]).

## [0.9.0-beta] - 2026-03-27

### Added

- **Typed pipeline architecture**: `ComponentBase<TRequest, TResponse>` as the core abstraction for all components, replacing `Dictionary<string, object>` signatures throughout the codebase
- **`ToolBase<TReq, TRes>`** generic tool base class with typed request/response, YAML defaults merging, and output filtering
- **`EvaluatorBase<TInput, TResult>`** and **`FlowStepBase<TInput, TOutput>`** typed base classes bridging interfaces with the typed pipeline
- **15+ built-in tools**: FileRead, FileWrite, WebScrape, HttpApi, JSON, CSV, PDF, XML, Database, GitHub, CodeExecution, and more in dedicated `Orkeon.Tools.*` projects
- **`SimpleCrewOrchestrator`** replacing the Akka.NET actor model with straightforward async/await orchestration
- **Semantic agent selection** using embedding-based similarity to match tasks to the most suitable agent
- **Strongly typed configurations**: `AgentConfiguration`, `TaskContext`, `LlmConfig`, and related value objects throughout Domain and Application layers
- **5 LLM providers**: OpenAI, Ollama, Anthropic, Azure OpenAI, and Groq — all HTTP-based implementations extending `HttpLlmProviderBase`
- **Memory providers**: Redis (with vector search), SQLite (long-term persistence), and InMemory (for development and testing)
- **Fluent Builder API**: `AgentBuilder`, `TaskBuilder`, `CrewBuilder`, and `FluentBuilderFactory` for ergonomic agent/crew construction
- **YAML configuration support**: full round-trip export/import for agents, tasks, crews, and tool schemas; `[FieldSchema]`, `[ComponentContract]`, and related attributes for schema generation
- **`ToolSchemaGenerator`**: auto-generates JSON/YAML schemas from typed `[FieldSchema]` attributes, with `$ref`-based nested type extraction
- **Tool validation framework**: security validation, rate limiting, and telemetry hooks on every tool execution
- **Batch tool execution** for parallel tool operations
- **Structured tool calling protocol** (JSON-based) with `ToolCallRequest<TParameters>` and `FunctionCallInfo`
- **CQRS pipeline**: commands and queries for Agent, Crew, and Task aggregates; `ValidatingCommandHandler` decorator; `UnitOfWork` integration for post-persistence domain event dispatch
- **Strongly-typed entity IDs**: 28 concrete `EntityId<T>` types (ULID-based) — `AgentId`, `TaskId`, `CrewId`, etc. — replacing primitive string identifiers
- **A2A (Agent-to-Agent) communication protocol**: `AgentCard`, discovery, `AgentCommunicationClient/Server`, `TaskRouter`, mTLS support, and DI integration (subsequently renamed to `AgentCommunication/`)
- **Session checkpointing**: `IStateStore`, `CheckpointManager`, `ResumeEngine` with three backing stores; time-travel checkpoint history with fork, replay, and diff
- **Cognitive memory system**: LLM-powered remember/recall with LanceDB embedded vector store support
- **Enterprise auth**: Azure AD and OIDC integration, claims-based authorization
- **Memory encryption at-rest**: AES-256-GCM encrypted Redis and SQLite decorators with key rotation
- **DLP (Data Loss Prevention)**: `PiiDetector` with 5-channel interceptors and per-channel policy
- **RAG data validation**: integrity checks, injection detection, provenance tracking, and quarantine
- **Agent kill switch**: `IAgentLifecycleManager` for controlled agent termination
- **`InMemoryAgentMemoryStoreRepository`** (Infrastructure) for fast in-process agent memory
- **E2E test project** (`Orkeon.Infrastructure.Tests`) with 10+ integration tests; `IConfiguration` wired into test DI container
- **XML documentation** on all public types across all projects (CS1591 enforcement enabled)
- **`ToolCallRequest<TParameters>`** generic typed tool protocol
- **Manual mock library** for LLM, Knowledge, Memory, Security, MCP, Process, and Tool interfaces — replaces Moq across the entire test suite
- **Clean Architecture + DDD audit reports** (ADRs) documenting architectural decisions and conformance

### Changed

- **Complete rename to Orkeon** (via the interim Arkeon name) across the entire codebase (solution file, namespaces, projects, docs, HTML, scripts, and examples)
- **Infrastructure layer redesigned** without Akka.NET: simple HTTP-based implementations, direct `async/await` service calls, standard dependency injection replacing the actor model
- **Domain encapsulation hardened**: private/internal constructors on all value objects and aggregate roots; `Restore()` factory methods for persistence; `IReadOnlyList<T>` replacing mutable `List<T>` on domain types
- **15+ anemic domain types converted** to immutable records (`init`-only properties)
- **Value objects refactored**: `AgentSkill`, `AgentCapability`, `AgentSelectionResult`, `CrewVariables`, `DomainValueObjects.cs` split into per-feature files; renamed duplicates (`MemoryEntity`, `TypedTaskContext`, `DelegationToolParameters`)
- **Domain reorganized feature-first**: Builders, Templates, Callbacks, DomainEvents, and ValueObjects moved to bounded-context folders; `TrainingScenario` moved to Training BC; `A2A/` renamed to `AgentCommunication/`
- **Application layer reorganized** feature-first: DTOs co-located with feature folders; dead infrastructure port interfaces removed; `KickoffAsync` extracted from `Crew` aggregate to the Application orchestrator
- **Infrastructure layer reorganized** by feature/BC: Persistence separated per-aggregate
- **`MemoryRelevanceRanker` renamed** to `MemoryRelevanceService`
- **`AgentStep` string ID replaced** with typed `AgentStepId`
- **`IRepository` simplified**: `GetAllAsync` and `IPredicateRepository` removed (AP4/R45)
- **`IAgent.Tools` typed** as `IReadOnlyList<IBaseTool>` (previously untyped)
- **CQRS handlers wired** into the ConsoleApp via the standard pipeline; DI lifetimes aligned
- **Test suite migrated** from Moq to manual mocks and from FluentAssertions to xUnit `Assert`; test methods renamed to `Should_When` convention; Handler-level directory organization
- **Trading tools migrated** to typed generic pipeline `TradingToolBase<TRequest, TResponse>`; tool definitions extracted to YAML
- **Examples updated** to use Fluent Builder API and Docker Model Runner LLM configuration
- **ComponentBase JSON serialization** extracted from Domain to Infrastructure (N1)
- **`ShouldRetain`/`ShouldPromoteToLongTerm`** made internal to enforce aggregate boundary (R35/R8)
- **`EntityMemory`/`EpisodicMemory`** made internal to the aggregate (R8/R35)
- **`MemoryItem` mutation methods** made internal (R35)
- **`AgentCapabilities`/`CrewOutput` constructors** privatized (R28)
- **`CrewInput` constructor** made internal (R10/R28)
- **`ToolResult`/`ToolUsageMetrics`** converted to init-only properties (R25)
- **Validation pipeline** wired in: `CreateTaskCommandValidator` handles `AgentId` validation; `ValidatingCommandHandler` decorates the CQRS chain
- **`UnitOfWork` try/finally guard** added to ensure domain events are dispatched after persistence even on exceptions (R21/R39)
- **`ITaskRepository`** scoped documented; `SaveChangesAsync` removed from repository, delegated to `IUnitOfWork`

### Fixed

- Infrastructure `ChatClient` adapter no longer overrides caller-supplied LLM configuration
- Empty task output in process strategies handled gracefully
- `IMemoryScope` and `IMemoryProviderFactory` registered in DI (defaults to `NullMemoryScope`)
- LLM DI registration corrected across all examples
- LLM `BaseUrl` changed from `host.docker.internal` to `localhost` in examples
- Missing `AgentBuilder`/`CrewTaskBuilder` `using` directives in email-management and research-assistant examples
- ClassicTrading example: raw strings wrapped with Value Object factories; `Agent.AgentId` renamed to `Agent.Id`; namespace corrections for `LlmConfig` and `ILlmProvider`
- Duplicate `MemoryProviderConfigDto` removed (kept in `Memory/`, removed from `Common/`)
- Duplicate `MemoryRelevanceRanker` stale reference cleaned up (R43/R50)
- Domain event dispatch order standardized; DI lifetimes aligned (N5/N6)
- `PromptShieldBuilder` tests fixed after `AgentBackstory` VO migration (null backstory handling)
- Post-refactoring compilation errors resolved across Infrastructure project
- CS4014 warning: async Timer callback wrapped with `try/catch` and discarded correctly
- Null safety and structured logging fixes (CS8604, CA1873) across Application and Infrastructure
- Code quality fixes: cognitive complexity (S3776), unused parameters (S1172), collapsed ifs (S1066), assertion improvements (xUnit2013, xUnit2032), and more

### Removed

- **Akka.NET dependency** and all actor-model code (cluster sharding, distributed data, CRDT, `CollaborationActor`)
- **All TODO comments** from the codebase
- **`sonar-project.properties`** file (caused scanner conflicts; all parameters now passed via CLI)
- **Legacy `CodeInterpreterTool`** (replaced by `SecureCodeInterpreterTool`)
- **`GetAllAsync` and `IPredicateRepository`** from `IRepository<T>` (AP4/R45)
- **5 dead infrastructure port interfaces** from Application layer (R16)
- **Moq and FluentAssertions** package references from all test projects
- **`[Obsolete]` `FunctionCall` dictionary property** replaced by `FunctionCallInfo` (T17)
- **Telemetry infrastructure** (`StartSpan` → migrated to `StartActivity`; unused telemetry packages removed)
- **Direct Domain usings** from ConsoleApp services (R51)

---

## [0.1.0-alpha] - 2025-09-21

Initial public development snapshot. Core domain model established in C# following Clean Architecture principles, as an independent implementation.

### Added

- Initial solution structure: `Domain`, `Application`, `Infrastructure`, `ConsoleApp` projects
- Core domain entities: `Agent` (Worker, Manager, Observer, Human), `Crew`, `CrewTask`
- `IBaseTool` interface and initial tool implementations
- `ILlmProvider` with OpenAI and Ollama HTTP implementations
- Basic memory abstractions (`IMemoryProvider`)
- Initial Akka.NET actor-based agent execution (later replaced)
- Communication protocols: Direct, Broadcast, Consensus, Feedback
- Redis memory provider with vector search
- SQLite persistence for long-term memory
- ClassicTrading example (agent crew for trading workflows)
- Standalone mode (no Redis required)
- Console application entry point

[Unreleased]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.3...HEAD
[1.0.0-rc.3]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.2...v1.0.0-rc.3
[1.0.0-rc.2]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.1...v1.0.0-rc.2
[1.0.0-rc.1]: https://github.com/Orkeon/orkeon/compare/v0.9.2-beta...v1.0.0-rc.1
[0.9.2-beta]: https://github.com/Orkeon/orkeon/compare/v0.9.1-beta.rc1...v0.9.2-beta
[0.9.1-beta]: https://github.com/Orkeon/orkeon/compare/v0.9.0-beta...v0.9.1-beta.rc1
[0.9.0-beta]: https://github.com/Orkeon/orkeon/compare/v0.1.0-alpha...v0.9.0-beta
[0.1.0-alpha]: https://github.com/Orkeon/orkeon/releases/tag/v0.1.0-alpha
