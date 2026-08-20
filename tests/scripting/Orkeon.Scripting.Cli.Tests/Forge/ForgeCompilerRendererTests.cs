using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The compiler (SPEC-ORKEON-FORGE §8): one source of truth — the DTOs the loader reads and
/// the configuration the shared validator judges derive from the same compilation, so what
/// is written and what is validated cannot drift.
/// </summary>
public class ForgeBlueprintCompilerTests
{
    private static ForgeBlueprint Parse(string json)
    {
        Assert.True(ForgeBlueprint.TryParse(json, out var blueprint, out var errors), string.Join("; ", errors));
        return blueprint!;
    }

    [Fact]
    public void A_valid_blueprint_compiles_and_passes_the_shared_validator()
    {
        var compilation = ForgeBlueprintCompiler.Compile(Parse(ForgeDocuments.ValidBlueprint));
        var verdict = ForgeBlueprintCompiler.Validate(compilation, ForgeDocuments.KnownTools);

        Assert.Empty(verdict.Errors);
        Assert.Equal("veille-fournisseur", compilation.Configuration.Name);
        Assert.Equal(2, compilation.Configuration.Agents.Count);
        Assert.Equal(2, compilation.Configuration.Tasks.Count);
        Assert.Equal("/output/resume.md", compilation.Tasks["resume"].Deliverable!.Path);
    }

    [Fact]
    public void An_unknown_tool_is_refused_naming_the_agent_and_the_tool()
    {
        var compilation = ForgeBlueprintCompiler.Compile(Parse(ForgeDocuments.ValidBlueprint));
        var verdict = ForgeBlueprintCompiler.Validate(compilation, ["file_read"]);

        Assert.Contains(verdict.Errors, e =>
            e.Contains("FORGE-TOOL-UNKNOWN", StringComparison.Ordinal)
            && e.Contains("collecteur", StringComparison.Ordinal)
            && e.Contains("web_scrape", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ghost_agent_reference_is_caught_before_the_mapping_erases_it()
    {
        // YamlCrewMapper maps an unresolvable `agent:` to AssignedAgentId = null, so the
        // shared validator cannot see the mistake — the pre-mapping check is the only one
        // that still can. Same story for a ghost dependency.
        var json = """
            {
              "crew": { "name": "x", "goal": "g" },
              "agents": [ { "key": "a", "role": "r", "goal": "g" } ],
              "tasks": [ { "key": "t", "description": "d", "expectedOutput": "o", "agent": "ghost", "dependencies": [ "nowhere" ] } ]
            }
            """;

        var compilation = ForgeBlueprintCompiler.Compile(Parse(json));
        var verdict = ForgeBlueprintCompiler.Validate(compilation, ForgeDocuments.KnownTools);

        Assert.Contains(verdict.Errors, e => e.Contains("'ghost'", StringComparison.Ordinal));
        Assert.Contains(verdict.Errors, e => e.Contains("'nowhere'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ghost_manager_is_caught_too()
    {
        var json = """
            {
              "crew": { "name": "x", "goal": "g", "process": "hierarchical" },
              "agents": [ { "key": "a", "role": "r", "goal": "g" } ],
              "tasks": [ { "key": "t", "description": "d", "expectedOutput": "o", "agent": "a" } ],
              "manager": "phantom"
            }
            """;

        var compilation = ForgeBlueprintCompiler.Compile(Parse(json));
        var verdict = ForgeBlueprintCompiler.Validate(compilation, ForgeDocuments.KnownTools);

        Assert.Contains(verdict.Errors, e => e.Contains("'phantom'", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_pass_through_without_blocking()
    {
        var json = """
            {
              "crew": { "name": "x", "goal": "g", "process": "hierarchical" },
              "agents": [ { "key": "a", "role": "r", "goal": "g" } ],
              "tasks": [ { "key": "t", "description": "d", "expectedOutput": "o", "agent": "a" } ]
            }
            """;

        var compilation = ForgeBlueprintCompiler.Compile(Parse(json));
        var verdict = ForgeBlueprintCompiler.Validate(compilation, ForgeDocuments.KnownTools);

        Assert.Empty(verdict.Errors);
        Assert.NotEmpty(verdict.Warnings);   // hierarchical without a manager is a warning
    }
}

/// <summary>
/// The renderer (SPEC-ORKEON-FORGE §8.1): per-entity layout, serialized from the very DTOs
/// the loader deserializes with the very serializer it uses — the round-trip test closes
/// the loop without touching the VFS.
/// </summary>
public sealed class ForgeYamlRendererTests : IDisposable
{
    private readonly string _sessionDirectory =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-render-" + Guid.NewGuid().ToString("N"));

    public ForgeYamlRendererTests() => Directory.CreateDirectory(_sessionDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_sessionDirectory))
            Directory.Delete(_sessionDirectory, recursive: true);
    }

    private static ForgeCompilation CompileValid()
    {
        Assert.True(ForgeBlueprint.TryParse(ForgeDocuments.ValidBlueprint, out var blueprint, out _));
        return ForgeBlueprintCompiler.Compile(blueprint!);
    }

    [Fact]
    public void The_per_entity_layout_is_written_one_file_per_entity()
    {
        var written = ForgeYamlRenderer.Render(CompileValid(), _sessionDirectory);

        Assert.Equal(
            [
                Path.Combine("crew", "config.yaml"),
                Path.Combine("crew", "agents", "collecteur.yaml"),
                Path.Combine("crew", "agents", "redacteur.yaml"),
                Path.Combine("crew", "tasks", "collecte.yaml"),
                Path.Combine("crew", "tasks", "resume.yaml"),
            ],
            written);

        foreach (var path in written)
            Assert.True(File.Exists(Path.Combine(_sessionDirectory, path)), path);
    }

    [Fact]
    public void The_rendered_files_round_trip_through_the_loader_serializer_and_revalidate()
    {
        ForgeYamlRenderer.Render(CompileValid(), _sessionDirectory);
        var crewDir = Path.Combine(_sessionDirectory, "crew");
        var serializer = new YamlDotNetSerializer();

        var settings = serializer.Deserialize<CrewSettingsYamlConfig>(
            File.ReadAllText(Path.Combine(crewDir, "config.yaml")));
        var agents = Directory.EnumerateFiles(Path.Combine(crewDir, "agents"), "*.yaml")
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => serializer.Deserialize<AgentYamlConfig>(File.ReadAllText(f)));
        var tasks = Directory.EnumerateFiles(Path.Combine(crewDir, "tasks"), "*.yaml")
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => serializer.Deserialize<TaskYamlConfig>(File.ReadAllText(f)));

        // Same mapper, same validator as the runtime path: the reloaded crew must be as
        // valid as the compiled one, or the render lied.
        var mapper = new YamlCrewMapper(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var reloaded = mapper.BuildConfiguration(
            new CrewMappingSettings
            {
                Name = settings.Name,
                Goal = settings.Goal,
                Process = settings.Process,
                ManagerAgent = settings.ManagerAgent,
            },
            agents!,
            tasks!);

        var verdict = CrewDefinitionValidator.Validate(reloaded);
        Assert.True(verdict.IsValid, string.Join("; ", verdict.Errors));
        Assert.Equal("veille-fournisseur", reloaded.Name);
        Assert.Equal(["collecte"], tasks["resume"].Dependencies!);
    }

    [Fact]
    public void A_re_render_leaves_no_stale_entity_behind()
    {
        ForgeYamlRenderer.Render(CompileValid(), _sessionDirectory);

        var smaller = """
            {
              "crew": { "name": "x", "goal": "g" },
              "agents": [ { "key": "solo", "role": "r", "goal": "g" } ],
              "tasks": [ { "key": "t", "description": "d", "expectedOutput": "o", "agent": "solo" } ]
            }
            """;
        Assert.True(ForgeBlueprint.TryParse(smaller, out var blueprint, out _));
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint!), _sessionDirectory);

        var agentFiles = Directory.EnumerateFiles(Path.Combine(_sessionDirectory, "crew", "agents"))
            .Select(Path.GetFileName);
        Assert.Equal(["solo.yaml"], agentFiles);
    }
}
