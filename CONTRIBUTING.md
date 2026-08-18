> 🇫🇷 [Version française](CONTRIBUTING.fr.md)

# Contributing to Orkeon

First off, thank you for considering contributing to Orkeon! It's people like you that make Orkeon such a great tool.

## Code of Conduct

This project and everyone participating in it is governed by our Code of Conduct. By participating, you are expected to uphold this code.

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

# Run tests
dotnet test Orkeon.sln
```

## Project Structure

```
src/
├── core/
│   ├── Orkeon.Domain/          # Core domain entities
│   ├── Orkeon.Application/     # Application services
│   └── Orkeon.Infrastructure/  # External integrations
├── tools/
│   ├── Orkeon.Tools.Abstractions/  # Tool base classes & interfaces
│   ├── Orkeon.Tools.Code/          # Code-related tools
│   ├── Orkeon.Tools.Data/          # Data manipulation tools
│   ├── Orkeon.Tools.FileSystem/    # File system tools
│   └── Orkeon.Tools.Web/           # Web/HTTP tools
├── plugins/
│   └── Orkeon.Plugins/        # Plugin system
└── apps/
    └── Orkeon.ConsoleApp/     # Console application

tests/
├── core/
│   ├── Orkeon.Domain.Tests/
│   ├── Orkeon.Application.Tests/
│   └── Orkeon.Infrastructure.Tests/
├── plugins/
│   └── Orkeon.Plugins.Tests/
└── shared/
    └── Orkeon.Tests.Shared/   # Common test fixtures

examples/                 # Example projects
docs/                    # Documentation
```

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

### Documentation

* Add XML documentation to all public APIs
* Include examples in documentation where helpful
* Update README.md if adding new features

#### Bilingual documentation (required)

The documentation is maintained in English and French in parallel. Any PR that adds, renames,
or removes a file under `docs/**.md` (outside `docs/fr/`) **must** make the matching change to
its French mirror under `docs/fr/`, and any change to a root community file (`README.md`,
`CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`) **must** update its `*.fr.md` mirror.
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

### High Priority
- [ ] ChromaDB memory provider implementation
- [ ] Pinecone memory provider
- [ ] Additional LLM providers (Anthropic, Cohere)
- [ ] Performance optimizations
- [ ] Documentation improvements

### Medium Priority
- [ ] Additional tools (Slack, Discord, Email)
- [ ] Streaming support
- [ ] Real-time agent communication
- [ ] Web UI for crew management

### Good First Issues
- [ ] Add more examples
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
  a non-experimental shipped API before 2.0. Until then (0.x), breaking changes may
  land in minor versions but are always called out in the CHANGELOG and the migration
  notes.

## Release Process

1. Update version numbers
2. Update CHANGELOG.md
3. Move `PublicAPI.Unshipped.txt` entries to `PublicAPI.Shipped.txt`
4. Create release notes
5. Tag the release
6. Build and publish NuGet packages

## Questions?

Feel free to open an issue with your question or reach out on Discord.

## License

By contributing, you agree that your contributions will be licensed under the MIT License.