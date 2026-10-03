using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-42 — the <c>guardrails:</c> a YAML agent declares reach its prompt. The loader mapped them onto
/// the configuration and <see cref="CrewFactory"/> never handed them to the agent it built: the system
/// prompt carried the task's guardrails alone, for every crew loaded from YAML, in every mode. The
/// factory passes them now — with the three templates of a configuration built in code — and a preset
/// the domain does not know, or a tool written twice in <c>toolRules</c>, fails the load naming the
/// agent or the task by its key, where a typo used to drop the preset's rules without a word.
/// </summary>
public sealed class AgentGuardrailsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string R1 = "Never invent the content of a file you could not read.";
    private const string T1 = "Read only the files under /src.";
    private const string R2 = "Cite every file the survey relies on.";

    /// <summary>The researcher's guardrails block, as an agent file or a crew file nests it.</summary>
    private const string ResearcherGuardrails = $$"""
        guardrails:
          preset: analysis
          rules: ["{{R1}}"]
          toolRules:
            file_read: ["{{T1}}"]
        """;

    private const string GuardedCrew = $$"""
        name: guarded-research
        goal: Survey the sources
        process: sequential
        agents:
          researcher:
            role: Researcher
            goal: Read the sources and report what they hold
            tools: [file_read]
            guardrails:
              preset: analysis
              rules: ["{{R1}}"]
              toolRules:
                file_read: ["{{T1}}"]
        tasks:
          survey:
            description: Survey the sources
            expectedOutput: A survey
            agent: researcher
            guardrails:
              rules: ["{{R2}}"]
        """;

    // ── the agent the factory builds ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_agents_guardrails_reach_the_agent_the_factory_builds()
    {
        var harness = new Harness();
        var crew = await harness.Factory.CreateFromConfigAsync(await harness.Loader.LoadFromStringAsync(GuardedCrew, Ct), Ct);

        var guardrails = (await harness.AgentAsync(crew)).Guardrails;

        // The preset's header and rules, then the agent's own; its tool rule beside the preset's.
        Assert.NotNull(guardrails);
        Assert.Equal(GuardrailDefaults.AnalysisHeader, guardrails.Header);
        Assert.Equal(GuardrailPresets.Analysis.Rules.Append(R1), guardrails.Rules);
        Assert.Equal([T1], guardrails.ToolRules["file_read"]);
        Assert.Equal(GuardrailPresets.Analysis.ToolRules["file_write"], guardrails.ToolRules["file_write"]);
        Assert.Equal(GuardrailPresets.Analysis.ToolRules["directory_read"], guardrails.ToolRules["directory_read"]);
    }

    [Fact]
    public async Task The_rendered_prompt_shows_the_agents_section_before_the_tasks()
    {
        var harness = new Harness();
        var crew = await harness.Factory.CreateFromConfigAsync(await harness.Loader.LoadFromStringAsync(GuardedCrew, Ct), Ct);
        var agent = await harness.AgentAsync(crew);
        var task = await harness.TaskAsync(crew);

        var prompt = Render(agent, task, TaskToolbelt.Compose(agent, task));

        AssertInOrder(prompt, GuardrailDefaults.AnalysisHeader, R1, $"[file_read] {T1}", GuardrailDefaults.DefaultHeader, R2);
    }

    [Fact]
    public async Task An_agents_tool_rule_shows_only_when_it_holds_the_tool()
    {
        var harness = new Harness();
        var crew = await harness.Factory.CreateFromConfigAsync(await harness.Loader.LoadFromStringAsync(GuardedCrew, Ct), Ct);
        var agent = await harness.AgentAsync(crew);
        var task = await harness.TaskAsync(crew);

        var prompt = Render(agent, task, toolbelt: []);

        AssertInOrder(prompt, GuardrailDefaults.AnalysisHeader, R1, R2);
        Assert.DoesNotContain(T1, prompt, StringComparison.Ordinal);
    }

    // ── end to end: what the model reads ─────────────────────────────────────────────────

    [Fact]
    public async Task A_run_sends_the_model_the_agents_guardrails_before_the_tasks()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult("The survey.");
        await using var container = Container(provider);
        await using var scope = container.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IToolRegistry>().RegisterToolAsync(new MockTool("file_read"));
        var config = await services.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(GuardedCrew, Ct);
        var crew = await services.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);

        var output = await services.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("guardrails", new Dictionary<string, object>()), Ct);

        Assert.True(output.Succeeded, output.Error);
        AssertInOrder(SystemMessage(provider), GuardrailDefaults.AnalysisHeader, R1, $"[file_read] {T1}", R2);
    }

    // ── the other layouts ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_per_entity_agent_file_brings_its_guardrails_to_the_built_agent()
    {
        var harness = new Harness();
        harness.FileSystem.AddFile("/crews/research/config.yaml", "name: guarded-research\ngoal: Survey the sources\nprocess: sequential");
        harness.FileSystem.AddFile("/crews/research/agents/researcher.yaml",
            "role: Researcher\ngoal: Read the sources\ntools: [file_read]\n" + ResearcherGuardrails);
        harness.FileSystem.AddFile("/crews/research/tasks/survey.yaml",
            "description: Survey the sources\nexpectedOutput: A survey\nagent: researcher");

        var crew = await harness.Factory.CreateFromDirectoryAsync("/crews/research", Ct);

        AssertResearcherGuardrails((await harness.AgentAsync(crew)).Guardrails);
    }

    [Fact]
    public async Task A_flat_directory_agents_file_brings_them_too()
    {
        var harness = new Harness();
        harness.FileSystem.AddFile("/crews/research/crew.yaml", "name: guarded-research\ngoal: Survey the sources\nprocess: sequential");
        harness.FileSystem.AddFile("/crews/research/agents.yaml",
            "researcher:\n  role: Researcher\n  goal: Read the sources\n  tools: [file_read]\n" + Indent(ResearcherGuardrails, "  "));
        harness.FileSystem.AddFile("/crews/research/tasks.yaml",
            "survey:\n  description: Survey the sources\n  expectedOutput: A survey\n  agent: researcher");

        var crew = await harness.Factory.CreateFromDirectoryAsync("/crews/research", Ct);

        AssertResearcherGuardrails((await harness.AgentAsync(crew)).Guardrails);
    }

    // ── the templates of a configuration built in code ───────────────────────────────────

    private const string SystemTemplate = "You are the house writer: short sentences, no jargon.";
    private const string PromptTemplate = "Prompt template that nothing reads.";
    private const string ResponseTemplate = "End every answer with one line that sums it up.";

    [Fact]
    public async Task A_configuration_built_in_code_keeps_its_templates_through_the_factory()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult("The note.");
        await using var container = Container(provider);
        await using var scope = container.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var writerId = AgentId.Create();
        var config = new CrewConfiguration
        {
            Name = "templated",
            Goal = "Write in the house style",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = writerId,
                    Role = "Writer",
                    Goal = "Write the note",
                    AllowDelegation = false,
                    SystemTemplate = SystemTemplate,
                    PromptTemplate = PromptTemplate,
                    ResponseTemplate = ResponseTemplate,
                },
            ],
            Tasks = [new TaskConfiguration { Description = "Write the note", ExpectedOutput = "A note", AssignedAgentId = writerId }],
        };

        var crew = await services.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);

        var agent = await services.GetRequiredService<IAgentRepository>().GetByIdAsync(Assert.Single(crew.Agents), Ct);
        Assert.NotNull(agent);
        Assert.Equal(SystemTemplate, agent.SystemTemplate);
        Assert.Equal(PromptTemplate, agent.PromptTemplate);
        Assert.Equal(ResponseTemplate, agent.ResponseTemplate);

        var output = await services.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("templates", new Dictionary<string, object>()), Ct);

        Assert.True(output.Succeeded, output.Error);
        var system = SystemMessage(provider);
        Assert.StartsWith(SystemTemplate, system, StringComparison.Ordinal);
        Assert.EndsWith(ResponseTemplate, system.TrimEnd(), StringComparison.Ordinal);
    }

    // ── what the load refuses ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_preset_on_an_agent_fails_the_load_naming_the_agent_and_the_known_presets()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewLoader().LoadFromStringAsync(CrewWith(agentGuardrails: "{ preset: analysys }"), Ct));

        Assert.Contains("Agent 'researcher'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'analysys'", error.Message, StringComparison.Ordinal);
        Assert.Contains("analysis, strict, creative", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_preset_on_a_task_fails_the_load_naming_the_task_and_the_known_presets()
    {
        // A preset with rules beside it used to keep the rules alone: the preset's were dropped.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewLoader().LoadFromStringAsync(CrewWith(taskGuardrails: $"{{ preset: analysys, rules: [\"{R2}\"] }}"), Ct));

        Assert.Contains("Task 'survey'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'analysys'", error.Message, StringComparison.Ordinal);
        Assert.Contains("analysis, strict, creative", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_written_twice_in_toolRules_fails_the_load_naming_it()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewLoader().LoadFromStringAsync(
                CrewWith(agentGuardrails: "{ toolRules: { file_write: [a], File_Write: [b] } }"), Ct));

        Assert.Contains("Agent 'researcher'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'File_Write'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_written_twice_in_a_tasks_toolRules_fails_the_load_naming_the_task()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewLoader().LoadFromStringAsync(
                CrewWith(taskGuardrails: "{ preset: strict, toolRules: { file_write: [a], FILE_WRITE: [b] } }"), Ct));

        Assert.Contains("Task 'survey'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'FILE_WRITE'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tool_written_without_rules_adds_none()
    {
        // It used to fail the load with a raw "Value cannot be null".
        var config = await NewLoader().LoadFromStringAsync(
            CrewWith(agentGuardrails: $"{{ rules: [\"{R1}\"], toolRules: {{ file_write: }} }}"), Ct);

        var guardrails = Assert.Single(config.Agents).Guardrails;
        Assert.NotNull(guardrails);
        Assert.Equal([R1], guardrails.Rules);
        Assert.Empty(guardrails.ToolRules["file_write"]);
    }

    [Theory]
    [InlineData("analysis")]
    [InlineData("Strict")]
    [InlineData("' creative '")]
    public async Task A_known_preset_loads_in_any_case(string preset)
    {
        var config = await NewLoader().LoadFromStringAsync(CrewWith(agentGuardrails: $"{{ preset: {preset} }}"), Ct);

        Assert.False(Assert.Single(config.Agents).Guardrails!.IsEmpty);
    }

    [Fact]
    public async Task A_blank_preset_is_no_preset()
    {
        var config = await NewLoader().LoadFromStringAsync(
            CrewWith(agentGuardrails: $"{{ preset: '', rules: [\"{R1}\"] }}"), Ct);

        var guardrails = Assert.Single(config.Agents).Guardrails;
        Assert.NotNull(guardrails);
        Assert.Null(guardrails.Header);
        Assert.Equal([R1], guardrails.Rules);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private static YamlCrewDefinitionLoader NewLoader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    /// <summary>A one-agent, one-task crew; each side's guardrails in YAML flow style, or none.</summary>
    private static string CrewWith(string? agentGuardrails = null, string? taskGuardrails = null) => $"""
        name: guarded-research
        goal: Survey the sources
        agents:
          researcher:
            role: Researcher
            goal: Read the sources
        {(agentGuardrails is null ? string.Empty : $"    guardrails: {agentGuardrails}")}
        tasks:
          survey:
            description: Survey the sources
            expectedOutput: A survey
            agent: researcher
        {(taskGuardrails is null ? string.Empty : $"    guardrails: {taskGuardrails}")}
        """;

    private static string Indent(string yaml, string by)
        => string.Join('\n', yaml.Split('\n').Select(line => by + line));

    private static void AssertResearcherGuardrails(GuardrailsConfig? guardrails)
    {
        Assert.NotNull(guardrails);
        Assert.Equal(GuardrailDefaults.AnalysisHeader, guardrails.Header);
        Assert.Equal(GuardrailPresets.Analysis.Rules.Append(R1), guardrails.Rules);
        Assert.Equal([T1], guardrails.ToolRules["file_read"]);
    }

    private static string Render(DomainAgent agent, CrewTask task, IReadOnlyList<IBaseTool> toolbelt)
    {
        var prompt = new StringBuilder();
        GuardrailsPromptRenderer.AppendAgentAndTaskGuardrails(prompt, agent, task, toolbelt);
        return prompt.ToString();
    }

    /// <summary>Each part appears in <paramref name="text"/>, each after the one before it.</summary>
    private static void AssertInOrder(string text, params string[] parts)
    {
        var from = 0;
        foreach (var part in parts)
        {
            var at = text.IndexOf(part, from, StringComparison.Ordinal);
            Assert.True(at >= 0, $"'{part}' is missing after position {from} of:\n{text}");
            from = at + part.Length;
        }
    }

    /// <summary>The system message of the provider's last call.</summary>
    private static string SystemMessage(MockLlmProvider provider)
        => Assert.Single(provider.LastChatMessages!, message => message.Role == LlmRoles.System).Content;

    /// <summary>A host on <paramref name="provider"/>, wired as the runners wire it.</summary>
    private static ServiceProvider Container(MockLlmProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => provider, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    /// <summary>The real loader and factory on in-memory repositories, <c>file_read</c> registered.</summary>
    private sealed class Harness
    {
        private readonly InMemoryAgentRepository _agents;
        private readonly InMemoryTaskRepository _tasks;

        public Harness()
        {
            var unitOfWork = new NullUnitOfWork();
            _agents = new InMemoryAgentRepository(unitOfWork);
            _tasks = new InMemoryTaskRepository(unitOfWork);
            var registry = new MockToolRegistry();
            registry.AddTool("file_read", new MockTool("file_read"));
            Loader = NewLoader(FileSystem);
            Factory = new CrewFactory(
                Loader,
                registry,
                NullLogger<CrewFactory>.Instance,
                new InMemoryCrewRepository(unitOfWork),
                _agents,
                _tasks,
                Options.Create(new CrewFactoryOptions { StrictTools = true }));
        }

        public FakeFileSystemService FileSystem { get; } = new();

        public YamlCrewDefinitionLoader Loader { get; }

        public CrewFactory Factory { get; }

        public async Task<DomainAgent> AgentAsync(DomainCrew crew)
            => (await _agents.GetByIdAsync(Assert.Single(crew.Agents), Ct))!;

        public async Task<CrewTask> TaskAsync(DomainCrew crew)
            => (await _tasks.GetByIdAsync(Assert.Single(crew.Tasks), Ct))!;
    }
}
