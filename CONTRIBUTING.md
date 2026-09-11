> 🇫🇷 [Version française](CONTRIBUTING.fr.md)

# Contributing to Orkeon

First off, thank you for considering contributing to Orkeon! It's people like you that make Orkeon such a great tool.

## Code of Conduct

This project and everyone participating in it is governed by our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check existing issues as you might find out that you don't need to create one. When you are creating a bug report, please include as many details as possible:

* **Use a clear and descriptive title**
* **Describe the exact steps which reproduce the problem**
* **Provide specific examples to demonstrate the steps**
* **Describe the behavior you observed after following the steps**
* **Explain which behavior you expected to see instead and why**
* **Include code samples and stack traces if applicable**

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion, please include:

* **Use a clear and descriptive title**
* **Provide a step-by-step description of the suggested enhancement**
* **Provide specific examples to demonstrate the steps**
* **Describe the current behavior and explain which behavior you expected to see instead**
* **Explain why this enhancement would be useful**

### Pull Requests

1. Fork the repo and create your branch from `main`
2. If you've added code that should be tested, add tests
3. If you've changed APIs, update the documentation
4. Ensure the test suite passes
5. Make sure your code follows the existing code style
6. Issue that pull request!
7. On your first pull request, accept the [Contributor License Agreement](CLA.md):
   a check posts the one sentence to reply with, and stays red until you do. Once per
   GitHub account; you keep your copyright, the project gets a licence it can carry to
   a successor entity. (The text is a template awaiting legal review — it says so on
   its first line — and the check documents the intended process meanwhile.)

## Development Setup

```bash
# Clone your fork
git clone https://github.com/your-username/orkeon.git
cd orkeon

# Add upstream remote
git remote add upstream https://github.com/Orkeon/orkeon.git

# Install dependencies
dotnet restore Orkeon.sln

# Build
dotnet build Orkeon.sln

# Run tests (the set CI runs)
dotnet test Orkeon.sln --filter "Category!=Integration&Category!=Slow"
```

> **Why the filter, and not a bare `dotnet test Orkeon.sln`**: `Category=Integration`
> covers the Testcontainers tests, which need Docker and pull gigabytes of database
> images, and `Category=Slow` carries the `Orkeon.Tools.Embeddings.Local` ONNX suite,
> whose native runtime takes the process down on teardown (exit 139) **after** every
> test has passed. `ci.yml` runs exactly the filtered command above, then runs that
> ONNX suite in a step of its own that tolerates that one crash shape.

> **Note** — this repository declares **private maintainer submodules**: clone
> **without** `--recursive` (as above). The build, the tests and the whole contribution
> workflow do not need them; a failing `git submodule update` on those paths is expected
> and harmless.

> **First build touches the network once**: the scripting layer bootstraps a small
> esbuild toolchain (`npm ci` under `tools/scripting-esbuild/`, strictly from the
> committed lockfile). To skip it (CI, no-npm machines):
> `dotnet build Orkeon.sln -p:SkipScriptingNpmInstall=true` — the build still
> succeeds and esbuild is resolved from `PATH` at runtime.

> **The tests run on Microsoft.Testing.Platform**, opted in by `global.json` — the .NET 10
> SDK refuses to run these projects through VSTest at all. Everyday commands are unchanged,
> but two things bite when you narrow a run: a `--filter` that matches **zero** tests in a
> module is an error (exit 8), not an empty success, so a solution-wide run must never
> exclude a project by name; and VSTest-only flags (`--collect`, `--logger`, `--blame`) are
> rejected as unknown arguments.

## Project Structure

The solution has **43 src projects across 13 zones** and **33 test projects**. Thirty-one
src projects are mirrored one-for-one by a test project; `tests/e2e` and `tests/shared` make
up the other two test projects. Twelve src projects carry no test mirror by design: the five
`Orkeon.Constants.*` satellites and `Orkeon.Rag.Onnx.Model` hold constants and embedded
resources only, `Orkeon.Analysis.Abstractions` is exercised through `Orkeon.Analysis.Tests`,
`Orkeon.Generators` is covered by the consuming projects' compilation, and the four
`src/packaging/` projects are packaging-only:

```
src/
├── core/        # Orkeon.Domain, Orkeon.Application, Orkeon.Infrastructure (Clean Architecture core)
├── tools/       # 9 tool packs: Abstractions, Analysis (RaggableTree), Code, Data,
│                #   Embeddings.Local, EventHub, FileSystem, Rag, Web
├── rag/         # RAG subsystem: Rag.Abstractions, Rag, Rag.Onnx, Rag.Onnx.Model
├── analysis/    # RaggableTree engine: Analysis.Abstractions, Analysis
├── scripting/   # Orkeon.Scripting (.ork.ts DSL) + Orkeon.Scripting.Cli (the `orkeon` tool)
├── cli/         # Cli.Abstractions, Cli, Cli.Commands.Scripting, Cli.TerminalGui
├── constants/   # Zero-dependency satellites of SHARED constants (ADR-009):
│                #   Constants.Llm, Constants.FileSystem, Constants.Configuration, Constants.Protocol, Constants.Cli
├── hosting/     # Orkeon.Hosting (RunnerHost) + Orkeon.Host (the `orkeon-host` daemon)
├── plugins/     # Orkeon.Plugins (runtime plugin loading)
├── generators/  # Orkeon.Generators (source generators)
├── analyzers/   # Orkeon.Compliance.Vfs (VFS-only Roslyn analyzer)
├── packaging/   # NuGet packaging projects (PUB-25): Orkeon (the framework in one nupkg), Orkeon.Tools,
│                #   + the Rag.Onnx / Tools.Embeddings.Local wrappers depending on the Orkeon umbrella
└── apps/        # Orkeon.ConsoleApp (orkeon-repl) + Orkeon.Studio.{Config,Core,Run,Wpf}

examples/        # 105 bundled examples (9 categories + showcases) — own solution
docs/            # Documentation, EN + docs/fr mirror (CI parity gate)
```

The full annotated tree lives in
[docs/getting-started/overview.md](docs/getting-started/overview.md#project-structure).

## Coding Standards

### C# Style Guide

* Use PascalCase for public members
* Use camelCase for private fields
* Prefix interfaces with 'I'
* Use meaningful variable names
* Keep methods small and focused
* Use async/await for asynchronous operations

### Example:
```csharp
public interface IAgentService
{
    Task<Agent> CreateAgentAsync(string role, string goal);
}

public class AgentService : IAgentService
{
    private readonly ILogger<AgentService> _logger;
    
    public async Task<Agent> CreateAgentAsync(string role, string goal)
    {
        // Implementation
    }
}
```

### Comments are English, and never accented

An invariant, enforced by `scripts/check-comment-accents.py` in CI: every comment is in
English and carries no accented letter. Studio's interface is in French, so the trap is a
comment quoting a UI label — **translate the label, do not strip its accents**: a comment
citing `"Modele d'IA"` names something the product never displays. Name the role instead
(`the model-settings tab`). An accent-stripped French sentence is still French, only worse.

Only comment lines are in scope. User-facing strings keep their accents. So does typography
the repository uses everywhere — em dashes, ellipses, arrows, guillemets: none of those are
accented letters.

### Documentation

* Add XML documentation to all public APIs
* Include examples in documentation where helpful
* Update README.md if adding new features

#### Bilingual documentation (required)

The documentation is maintained in English and French in parallel. Any PR that adds, renames,
or removes a file under `docs/**.md` (outside `docs/fr/`) **must** make the matching change to
its French mirror under `docs/fr/`, and any change to a root community file (`README.md`,
`CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md`, and the site landing
page `index.md`) **must** update its `*.fr.md` mirror.
The `scripts/check-docs-parity.sh` script verifies this: it fails when a mirror is missing —
run it locally before opening the PR. **This is a CI gate**: `ci.yml` runs the script on
every push and pull request, so a missing mirror fails the build. The script checks file existence, not content
equivalence — keeping the two in sync is on you; if you cannot translate immediately, add a
stub mirror and flag it for translation.

Dated audit snapshots (e.g. GO/NO-GO publication reports) are governance records, not
living documentation: they live in the maintainers' private governance repository, outside
`docs/`, so the parity contract applies to the whole documentation tree without exception.

### Testing

* Write unit tests for new functionality
* Maintain or improve code coverage
* Use descriptive test names
* Follow AAA pattern (Arrange, Act, Assert)

```csharp
[Fact]
public async Task Agent_Should_Execute_Task_Successfully()
{
    // Arrange
    var agent = Agent.Create("Researcher", "Find information");
    var task = CrewTask.Create("Research AI", "Report");
    
    // Act
    var result = await agent.ExecuteTaskAsync(task);
    
    // Assert
    Assert.True(result.Success);
    Assert.NotNull(result.Output);
}
```

## Areas for Contribution

### The scope is frozen

Orkeon already ships 14 LLM providers, 79 built-in tools, 6 memory stores, two
RAG pipelines, RaggableTree, a scripting DSL, plugins, MCP, A2A and a Studio —
maintained by one person. Until real users ask for more, **the functional
surface does not grow**:

- no 15th LLM provider — the OpenAI-compatible base covers any endpoint that
  speaks that dialect; point `Orkeon:Llm:BaseUrl` at it;
- no new built-in tool — write yours in a `.ork.ts` script or a plugin, both
  are first-class and need no change here;
- no new memory store, language adapter, or orchestration mode.

A pull request adding one of these will be closed with a link to this section,
whatever its quality. What *is* welcome is everything that lowers the cost of
trying Orkeon or of trusting it: bugs, tests, documentation, interoperability
with what people already use (Microsoft Agent Framework, OpenTelemetry, .NET
Aspire), and performance.

### High Priority
- [ ] Bugs found by running the examples against a local model
- [ ] MCP interop testing against reference servers (MCP Inspector)
- [ ] Performance optimizations
- [ ] Documentation improvements (see the EN/FR parity contract above)

### Good First Issues
- [ ] Add more examples (follow [the example README template](docs/templates/example-readme.md))
- [ ] Improve error messages
- [ ] Add XML documentation
- [ ] Fix typos in documentation

## Versioning and API stability

Orkeon follows [Semantic Versioning 2.0](https://semver.org/). The public API is not a
matter of opinion — it is **recorded in the repository** and enforced at build time:

- Every packable project carries `PublicAPI.Shipped.txt` (the frozen, released surface)
  and `PublicAPI.Unshipped.txt` (additions since the last release), checked by
  `Microsoft.CodeAnalysis.PublicApiAnalyzers`. An undeclared public API change fails
  the build (`RS0016`/`RS0017` are promoted to errors).
- **Adding** a public API: declare it in `PublicAPI.Unshipped.txt` (the analyzer's code
  fix does it for you — `dotnet format analyzers --diagnostics RS0016` on the project).
  At release time, `Unshipped` entries move to `Shipped`.
- **A breaking change is any edit or removal of a line in `PublicAPI.Shipped.txt`.**
  It requires a major version bump (a minor is acceptable only before 1.0), a
  `*REMOVED*` entry in the API file, and a CHANGELOG entry that says so plainly.
- **Deprecation window**: nothing public is removed without shipping `[Obsolete]` for
  at least one minor version first, with the replacement named in the message.
- **`[Experimental]` surfaces are outside this commitment.** A2A, Autonomous
  orchestration, corrective RAG and the MCP integration carry
  `[Experimental("ORKEXP00x")]` diagnostics: referencing them is a compile error you
  suppress explicitly, which is your opt-in to a surface that may change in any
  release. See [docs/reference/experimental-apis.md](docs/reference/experimental-apis.md).
- **Stability commitment for the 1.x window**: once 1.0 ships, no breaking change to
  a non-experimental shipped API before 2.0. Until then — the `1.0.0-rc.*` line
  that `main` is on today — breaking changes may still land between release
  candidates, but are always called out in the CHANGELOG and the migration notes.

## Release Process

1. Bump `VersionPrefix`/`VersionSuffix` in `src/Directory.Build.props` — the single
   source of truth. The publish workflow **refuses a `v*` tag that does not match it**.
2. Cut the `[Unreleased]` section of `CHANGELOG.md` into a dated version section.
3. Move `PublicAPI.Unshipped.txt` entries to `PublicAPI.Shipped.txt`.
4. Move the analyzer rules the same way: any entry pending in
   `src/analyzers/Orkeon.Compliance.Vfs/AnalyzerReleases.Unshipped.md` (the `ORKVFS00x`
   VFS-compliance rules) goes into `AnalyzerReleases.Shipped.md` under a
   `## Release <version>` heading, leaving the `Unshipped` file with its header only.
   The release-tracking analyzer parses that heading as a plain `Major.Minor.Patch`
   number and rejects a prerelease suffix (RS2007), so the heading for the 1.0.0 line
   is `## Release 1.0.0` — it is already there, and the `rc.*` tags add nothing to it.
5. Make sure CI is green. Beyond the `-warnaserror` build and the test suites, the
   gates that must pass are `scripts/check-docs-parity.sh`,
   `scripts/check-doc-claims.py`, `scripts/check-comment-accents.py`,
   `scripts/check-release-readiness.py` (it refuses a release whose
   `PublicAPI.Unshipped.txt` files are not header-only), `scripts/check-package-closure.py`,
   the examples linters (`scripts/generate-examples-index.sh --check`,
   `scripts/lint-example-configs.py`, `scripts/lint-example-readmes.py`), the executable-bit
   gate (`file-modes.yml`), the secret scan (`secret-scan.yml`) and the strict docfx build.
6. Tag `v<version>` and push the tag. This triggers: `publish.yml` (packs everything;
   pushes the **nine-package v1 lineup to NuGet.org** — `Orkeon`, `Orkeon.Tools`,
   `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model`, `Orkeon.Tools.Embeddings.Local`,
   `Orkeon.Scripting.Cli`, `Orkeon.Compliance.Vfs`, `Orkeon.Interop.AgentFramework`,
   `Orkeon.Hosting.Aspire`, the umbrella first because the others depend on it — and
   every packable to GitHub Packages; see
   [the publication matrix](docs/reference/publication-matrix.md)),
   `release.yml` (the per-platform CLI packages and multi-app archives for every RID,
   the Windows per-user MSI and the `orkeon-host` service MSI, the macOS tarballs, the
   Debian package and their checksum files — each smoked on a real runner before the
   Release is published — plus the `orkeon-runners` container image pushed to GHCR)
   and `docs.yml`
   (deploys the documentation site to GitHub Pages, at <https://orkeon.github.io/orkeon/>).

## Questions?

Feel free to open an issue, or start a thread in [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions).

## License

By contributing, you agree that your contributions will be licensed under the MIT License.