using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// GAP-19, decision 1 — the manager of a crew runs on the LLM the crew gives it: the provider C#
/// sets with <c>WithManagerLlm</c> (CrewAI's <c>manager_llm</c>), else its manager agent's own
/// <c>llm:</c> block (profile and model, a model left unset being the profile's own), else the host's
/// default profile. The hierarchical manager used to assign and review on the default whatever the
/// manager agent named, and <c>Crew.ManagerLlm</c> was validated, then never read — a crew that
/// gave it and no manager agent saw its first agent promoted to manager, and lost it as a worker.
/// </summary>
public sealed partial class ManagerLlmTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^- ID: (\S+)", RegexOptions.Multiline)]
    private static partial Regex ListedAgentId();

    /// <summary>
    /// A vendor that answers the manager's two prompts — an assignment to the first agent it lists,
    /// an approval — and any other prompt with a task's answer, recording which kind each call was.
    /// </summary>
    private sealed class Vendor
    {
        public Vendor(string name)
        {
            Provider = new MockLlmProvider { Name = name };
            Provider.SetChatFunc((messages, config) =>
            {
                var prompt = string.Join("\n", messages.Select(m => m.Content));
                if (prompt.Contains("responsible for assigning tasks", StringComparison.Ordinal))
                {
                    Calls.Add("assign");
                    AssignPrompts.Add(prompt);
                    ManagerModels.Add(config?.Model);
                    var first = ListedAgentId().Match(prompt).Groups[1].Value;
                    return Answer($$"""{"agent_id": "{{first}}", "reason": "listed first"}""");
                }

                if (prompt.Contains("reviewing the output of a completed task", StringComparison.Ordinal))
                {
                    Calls.Add("review");
                    ManagerModels.Add(config?.Model);
                    return Answer("""{"approved": true, "feedback": ""}""");
                }

                Calls.Add("task");
                return Answer("done");
            });
        }

        public MockLlmProvider Provider { get; }

        public List<string> Calls { get; } = [];

        public List<string> AssignPrompts { get; } = [];

        /// <summary>The model each manager call asked for, in order.</summary>
        public List<string?> ManagerModels { get; } = [];

        public IEnumerable<string> ManagerCalls => Calls.Where(c => c is "assign" or "review");

        private static LlmResponse Answer(string content) =>
            new() { Content = content, PromptTokens = 10, CompletionTokens = 2, TokensUsed = 12 };
    }

    private static ServiceProvider Host(Vendor @default, Vendor b, MockLlmUsageSink? sink = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        if (sink is not null)
            services.AddSingleton<ILlmUsageSink>(sink);
        services.AddOrkeonLlmProvider(_ => @default.Provider, LlmConfig.Create("model-of-a"));
        services.AddOrkeonLlmProfile("b", _ => b.Provider, LlmConfig.Create("model-of-b"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static CrewInput Input() => new("managed", new Dictionary<string, object>());

    private static async Task<CrewOutput> RunYamlAsync(Vendor @default, Vendor b, string chefLlm)
    {
        var yaml = $$"""
            name: managed
            goal: Ship the article
            process: hierarchical
            managerAgent: chef
            agents:
              chef:
                role: Chef
                goal: Lead the team
            {{chefLlm}}
              writer:
                role: Writer
                goal: Write the article
            tasks:
              write:
                description: Write the article
                expected_output: An article
            """;

        await using var container = Host(@default, b);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(crew.Id, Input(), Ct);
    }

    /// <summary>The agents a C# crew is built from: a chef on profile <c>b</c>, and two workers.</summary>
    private sealed record Team(DomainAgent Chef, DomainAgent Writer, DomainAgent Reviewer);

    /// <summary>Runs a crew built in C#: the team and two tasks saved, then kicked off.</summary>
    private static async Task<CrewOutput> RunBuiltAsync(
        Vendor @default, Vendor b, Func<CrewBuilder, Team, CrewBuilder> shape, MockLlmUsageSink? sink = null)
    {
        await using var container = Host(@default, b, sink);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var team = new Team(
            new AgentBuilder().Role("Chef").Goal("Lead the team").WithLlmConfig(LlmConfig.OnProfile("b")).Build(),
            new AgentBuilder().Role("Writer").Goal("Write the article").Build(),
            new AgentBuilder().Role("Reviewer").Goal("Review the article").Build());
        var draft = new CrewTaskBuilder().Description("Draft the article").ExpectedOutput("A draft").Build();
        var proof = new CrewTaskBuilder().Description("Proofread the article").ExpectedOutput("A clean text").Build();
        var crew = shape(new CrewBuilder().Goal("Ship the article"), team).Build();
        crew.AddTask(draft.Id);
        crew.AddTask(proof.Id);

        foreach (var agent in new[] { team.Chef, team.Writer, team.Reviewer })
            await sp.GetRequiredService<IAgentRepository>().AddAsync(agent, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(draft, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(proof, Ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);

        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(crew.Id, Input(), Ct);
    }

    // ── YAML: the manager agent's llm: block ──────────────────────────────────────────────

    [Fact]
    public async Task A_manager_agent_on_a_profile_assigns_and_reviews_on_that_profile_only()
    {
        var (a, b) = (new Vendor("vendor-a"), new Vendor("vendor-b"));

        var output = await RunYamlAsync(a, b, """
                llm:
                  profile: b
            """);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "review"], b.Calls);
        Assert.Empty(a.ManagerCalls);
        // The worker stays on the default: only the manager moved.
        Assert.Equal(["task"], a.Calls);
        // No model named: the calls name none, and the profile's provider runs on its own (GAP-18).
        Assert.All(b.ManagerModels, Assert.Null);
    }

    [Fact]
    public async Task A_manager_agent_naming_a_model_gets_it_on_its_profile()
    {
        var (a, b) = (new Vendor("vendor-a"), new Vendor("vendor-b"));

        var output = await RunYamlAsync(a, b, """
                llm:
                  profile: b
                  model: chef-model
            """);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["chef-model", "chef-model"], b.ManagerModels);
    }

    [Fact]
    public async Task A_manager_agent_naming_a_model_alone_gets_it_on_the_default_profile()
    {
        var (a, b) = (new Vendor("vendor-a"), new Vendor("vendor-b"));

        var output = await RunYamlAsync(a, b, """
                llm:
                  model: chef-model
            """);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "review"], a.ManagerCalls);
        Assert.Equal(["chef-model", "chef-model"], a.ManagerModels);
        Assert.Empty(b.Calls);
    }

    [Fact]
    public async Task A_manager_agent_without_an_llm_block_manages_on_the_default_profile()
    {
        var (a, b) = (new Vendor("vendor-a"), new Vendor("vendor-b"));

        var output = await RunYamlAsync(a, b, "");

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "task", "review"], a.Calls);
        Assert.All(a.ManagerModels, Assert.Null);
        Assert.Empty(b.Calls);
    }

    [Fact]
    public async Task A_manager_agent_naming_a_profile_the_host_does_not_offer_fails_the_load()
    {
        var (a, b) = (new Vendor("vendor-a"), new Vendor("vendor-b"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RunYamlAsync(a, b, """
                llm:
                  profile: claude
            """));

        Assert.Contains("'claude'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, b.", error.Message, StringComparison.Ordinal);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public async Task A_yaml_hierarchical_crew_without_a_manager_agent_fails_its_load_naming_the_key()
    {
        // The schema promised "omitted → the first agent manages" and the loader warned so; the
        // builder refused the crew anyway, asking for a "manager LLM" YAML cannot express.
        const string yaml = """
            name: unmanaged
            goal: Ship the article
            process: hierarchical
            agents:
              writer:
                role: Writer
                goal: Write the article
            tasks:
              write:
                description: Write the article
                expected_output: An article
            """;
        await using var container = Host(new Vendor("vendor-a"), new Vendor("vendor-b"));
        await using var scope = container.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct));

        Assert.Contains("managerAgent", error.Message, StringComparison.Ordinal);
    }

    // ── C#: Crew.ManagerLlm ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_csharp_crew_with_a_manager_llm_and_no_manager_agent_manages_on_that_provider_alone()
    {
        var (a, b, c) = (new Vendor("vendor-a"), new Vendor("vendor-b"), new Vendor("vendor-c"));
        var sink = new MockLlmUsageSink();

        var output = await RunBuiltAsync(a, b,
            (crew, team) => crew.Hierarchical().WithManagerLlm(c.Provider).WithAgent(team.Writer).WithAgent(team.Reviewer),
            sink);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "review", "assign", "review"], c.Calls);
        Assert.Empty(a.ManagerCalls);
        Assert.Empty(b.Calls);
        // No agent was made manager: both are offered to the manager as workers.
        Assert.All(c.AssignPrompts, prompt =>
        {
            Assert.Contains("Role: Writer", prompt, StringComparison.Ordinal);
            Assert.Contains("Role: Reviewer", prompt, StringComparison.Ordinal);
        });
        // Metered like a provider the host registers, as the manager's work.
        var managerUsage = sink.Recorded.Where(e => e.Provider == "vendor-c").ToList();
        Assert.Equal(4, managerUsage.Count);
        Assert.All(managerUsage, e => Assert.Equal(LlmUsageOperations.Manager, e.OperationType));
    }

    [Fact]
    public async Task A_csharp_manager_agent_on_a_profile_manages_on_that_profile()
    {
        var (a, b) = (new Vendor("vendor-a"), new Vendor("vendor-b"));

        var output = await RunBuiltAsync(a, b,
            (crew, team) => crew.Hierarchical(team.Chef).WithAgent(team.Chef).WithAgent(team.Writer).WithAgent(team.Reviewer));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "review", "assign", "review"], b.Calls);
        Assert.Empty(a.ManagerCalls);
        Assert.All(b.AssignPrompts, prompt => Assert.DoesNotContain("Role: Chef", prompt, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_manager_llm_wins_over_the_manager_agents_profile()
    {
        var (a, b, c) = (new Vendor("vendor-a"), new Vendor("vendor-b"), new Vendor("vendor-c"));

        var output = await RunBuiltAsync(a, b, (crew, team) => crew
            .Hierarchical(team.Chef).WithManagerLlm(c.Provider)
            .WithAgent(team.Chef).WithAgent(team.Writer).WithAgent(team.Reviewer));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "review", "assign", "review"], c.Calls);
        Assert.Empty(b.Calls);
        // The manager agent still manages: it is no worker.
        Assert.All(c.AssignPrompts, prompt => Assert.DoesNotContain("Role: Chef", prompt, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_autonomous_crew_with_a_manager_llm_hands_its_tasks_out_on_that_provider()
    {
        var (a, b, c) = (new Vendor("vendor-a"), new Vendor("vendor-b"), new Vendor("vendor-c"));

        var output = await RunBuiltAsync(a, b, (crew, team) => crew
            .Process(ProcessType.Autonomous).WithManagerLlm(c.Provider).WithAgent(team.Writer).WithAgent(team.Reviewer));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["assign", "assign"], c.Calls);
        Assert.Empty(a.ManagerCalls);
        Assert.Equal(["task", "task"], a.Calls);
    }
}
