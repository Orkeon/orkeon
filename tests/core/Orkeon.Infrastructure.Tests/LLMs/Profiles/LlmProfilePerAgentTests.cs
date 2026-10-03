using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// GAP-17: one LLM provider per agent. The host names its providers as profiles
/// (<c>Llm:Profiles:&lt;name&gt;</c>, or <see cref="LlmProviderRegistrationExtensions.AddOrkeonLlmProfile"/>);
/// a crew picks one by name — at crew, agent or task level — and each agent's calls reach
/// that profile's provider, and only it. Before this lot every agent of every crew went
/// through the one provider the host's <c>Llm</c> section described. GAP-34: an agent built in C#
/// may carry its own provider (<c>AgentBuilder.WithLlm</c>, <c>Agent.Llm</c>) — its turns run there,
/// metered once, unless its task's <c>llm_override</c> names a profile.
/// </summary>
public sealed class LlmProfilePerAgentTests
{
    private sealed record Run(
        CrewOutput Output,
        MockLlmProvider Default,
        MockLlmProvider A,
        MockLlmProvider B,
        MockLlmUsageSink Sink);

    private static MockLlmProvider Provider(string name, string answer)
    {
        var provider = new MockLlmProvider { Name = name };
        provider.SetChatResult(new LlmResponse { Content = answer, PromptTokens = 10, CompletionTokens = 2, TokensUsed = 12 });
        return provider;
    }

    private static ServiceCollection Services(MockLlmProvider @default, MockLlmProvider a, MockLlmProvider b, MockLlmUsageSink sink)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddSingleton<ILlmUsageSink>(sink);
        services.AddOrkeonLlmProvider(_ => @default, LlmConfig.Create("host-model"));
        services.AddOrkeonLlmProfile("a", _ => a, LlmConfig.Create("model-of-a"));
        services.AddOrkeonLlmProfile("b", _ => b, LlmConfig.Create("model-of-b"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services;
    }

    private static async Task<Run> RunAsync(string yaml, CancellationToken ct)
    {
        var @default = Provider("vendor-default", "default answer");
        var a = Provider("vendor-a", "answer from a");
        var b = Provider("vendor-b", "answer from b");
        var sink = new MockLlmUsageSink();

        await using var container = Services(@default, a, b, sink).BuildServiceProvider();
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, ct);
        var crew = await container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, ct);
        var output = await container.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("profiles", new Dictionary<string, object>()), ct);

        return new Run(output, @default, a, b, sink);
    }

    private const string TwoProfilesYaml = """
        name: two-profiles
        goal: Plan on one vendor, write on another
        process: sequential
        agents:
          planner:
            role: Planner
            goal: Plan the work
            llm:
              profile: a
              model: planner-model
          writer:
            role: Writer
            goal: Write the result
            llm:
              profile: b
        tasks:
          plan:
            description: Plan the article
            expected_output: A plan
            agent: planner
          write:
            description: Write the article
            expected_output: An article
            agent: writer
        """;

    [Fact]
    public async Task Two_agents_on_two_profiles_each_reach_only_their_own_provider()
    {
        var run = await RunAsync(TwoProfilesYaml, TestContext.Current.CancellationToken);

        Assert.Equal(2, run.Output.TaskOutputs.Count);
        Assert.Equal(1, run.A.ChatCallCount);
        Assert.Equal(1, run.B.ChatCallCount);
        Assert.Equal(0, run.Default.ChatCallCount);
        Assert.Contains("Plan the article", string.Join("\n", run.A.LastChatMessages!.Select(m => m.Content)));
        Assert.Contains("Write the article", string.Join("\n", run.B.LastChatMessages!.Select(m => m.Content)));
    }

    [Fact]
    public async Task An_agent_naming_a_model_gets_it_and_one_naming_none_gets_its_profiles_own()
    {
        var run = await RunAsync(TwoProfilesYaml, TestContext.Current.CancellationToken);

        Assert.Equal("planner-model", run.A.LastChatConfig!.Model);
        Assert.Equal("model-of-b", run.B.LastChatConfig!.Model);
    }

    [Fact]
    public async Task The_meter_attributes_each_call_to_the_provider_of_its_profile()
    {
        var run = await RunAsync(TwoProfilesYaml, TestContext.Current.CancellationToken);

        var byProvider = run.Sink.Recorded
            .GroupBy(e => e.Provider)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(1, byProvider["vendor-a"]);
        Assert.Equal(1, byProvider["vendor-b"]);
        Assert.False(byProvider.ContainsKey("vendor-default"));
    }

    [Fact]
    public async Task An_agent_without_a_profile_stays_on_the_host_default()
    {
        const string yaml = """
            name: default-only
            goal: Run on the default
            process: sequential
            agents:
              worker:
                role: Worker
                goal: Work
                llm:
                  temperature: 0.2
            tasks:
              work:
                description: Do the work
                expected_output: Work done
                agent: worker
            """;

        var run = await RunAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(1, run.Default.ChatCallCount);
        Assert.Equal(0, run.A.ChatCallCount + run.B.ChatCallCount);
        // No model named: the host's own, never the framework's default model.
        Assert.Equal("host-model", run.Default.LastChatConfig!.Model);
        Assert.Equal(0.2, run.Default.LastChatConfig.Temperature, precision: 3);
    }

    [Fact]
    public async Task A_crew_level_profile_reaches_every_agent_and_an_agent_can_return_to_the_default()
    {
        const string yaml = """
            name: crew-profile
            goal: Run on a
            process: sequential
            llm:
              profile: a
            agents:
              first:
                role: First
                goal: Go first
              second:
                role: Second
                goal: Go second
                llm:
                  profile: default
            tasks:
              one:
                description: First task
                expected_output: Done
                agent: first
              two:
                description: Second task
                expected_output: Done
                agent: second
            """;

        var run = await RunAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(1, run.A.ChatCallCount);
        Assert.Equal(1, run.Default.ChatCallCount);
        Assert.Equal(0, run.B.ChatCallCount);
    }

    [Fact]
    public async Task A_task_llm_override_moves_that_task_alone_to_another_profile()
    {
        const string yaml = """
            name: task-override
            goal: Mostly a, once b
            process: sequential
            agents:
              worker:
                role: Worker
                goal: Work
                llm:
                  profile: a
            tasks:
              usual:
                description: Usual task
                expected_output: Done
                agent: worker
              special:
                description: Special task
                expected_output: Done
                agent: worker
                llm_override:
                  profile: b
            """;

        var run = await RunAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(1, run.A.ChatCallCount);
        Assert.Equal(1, run.B.ChatCallCount);
        Assert.Contains("Special task", string.Join("\n", run.B.LastChatMessages!.Select(m => m.Content)));
    }

    [Fact]
    public async Task An_unknown_profile_fails_the_load_and_lists_the_known_ones()
    {
        const string yaml = """
            name: typo
            goal: Name a profile the host does not define
            process: sequential
            agents:
              worker:
                role: Worker
                goal: Work
                llm:
                  profile: claude
            tasks:
              work:
                description: Do the work
                expected_output: Work done
                agent: worker
            """;
        var ct = TestContext.Current.CancellationToken;
        var sink = new MockLlmUsageSink();
        await using var container = Services(Provider("d", "x"), Provider("a", "x"), Provider("b", "x"), sink).BuildServiceProvider();
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, ct));

        Assert.Contains("'claude'", error.Message, StringComparison.Ordinal);
        Assert.Contains("default, a, b", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_profile_in_a_task_override_fails_the_load_too()
    {
        const string yaml = """
            name: typo
            goal: Name a profile the host does not define
            process: sequential
            agents:
              worker:
                role: Worker
                goal: Work
            tasks:
              work:
                description: Do the work
                expected_output: Work done
                agent: worker
                llm_override:
                  profile: nope
            """;
        var ct = TestContext.Current.CancellationToken;
        await using var container = Services(Provider("d", "x"), Provider("a", "x"), Provider("b", "x"), new MockLlmUsageSink())
            .BuildServiceProvider();
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, ct));

        Assert.Contains("'nope'", error.Message, StringComparison.Ordinal);
    }

    // ── C#: an agent on its own provider (GAP-34) ────────────────────────────────────────

    /// <summary>
    /// Runs one review task, built in C#, on <paramref name="agent"/> — the task moved to
    /// <paramref name="taskOverride"/>'s profile when it names one — on the host of this suite.
    /// </summary>
    private static async Task<(CrewOutput Output, MockLlmProvider Default, MockLlmProvider B)> RunBuiltAsync(
        DomainAgent agent, MockLlmUsageSink sink, LlmConfigOverride? taskOverride = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var @default = Provider("vendor-default", "default answer");
        var b = Provider("vendor-b", "answer from b");
        await using var container = Services(@default, Provider("vendor-a", "answer from a"), b, sink).BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var task = new CrewTaskBuilder().Description("Review the change").ExpectedOutput("A review")
            .AssignTo(agent).WithLlmOverride(taskOverride).Build();
        var crew = new CrewBuilder().Goal("Review the change").Sequential().WithAgent(agent).WithTask(task).Build();
        await sp.GetRequiredService<IAgentRepository>().AddAsync(agent, ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(task, ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, ct);

        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("own-provider", new Dictionary<string, object>()), ct);
        return (output, @default, b);
    }

    [Fact]
    public async Task A_csharp_agent_on_its_own_provider_runs_its_turns_there_counted_once_as_its_work()
    {
        // WithLlm used to set a field nothing read: the default answered, without a word.
        var own = Provider("vendor-own", "answer from its own");
        var sink = new MockLlmUsageSink();
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review the change").WithLlm(own).Build();

        var (output, @default, _) = await RunBuiltAsync(agent, sink);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal("answer from its own", output.FinalOutput);
        Assert.Equal(1, own.ChatCallCount);
        Assert.Equal(0, @default.ChatCallCount);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal("vendor-own", usage.Provider);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal("Reviewer", usage.AgentId);
    }

    [Fact]
    public async Task An_own_provider_metered_already_is_read_once_per_call()
    {
        var own = Provider("vendor-own", "answer from its own");
        var sink = new MockLlmUsageSink();
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review the change")
            .WithLlm(MeteredLlmProvider.Wrap(own, sink)).Build();

        var (output, _, _) = await RunBuiltAsync(agent, sink);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal("vendor-own", Assert.Single(sink.Recorded).Provider);
    }

    [Fact]
    public async Task A_task_llm_override_profile_moves_that_task_off_the_agents_own_provider()
    {
        var own = Provider("vendor-own", "answer from its own");
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review the change").WithLlm(own).Build();

        var (output, @default, b) = await RunBuiltAsync(agent, new MockLlmUsageSink(), new LlmConfigOverride { Profile = "b" });

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(1, b.ChatCallCount);
        Assert.Equal(0, own.ChatCallCount);
        Assert.Equal(0, @default.ChatCallCount);
    }

    [Fact]
    public async Task A_task_llm_override_naming_the_default_brings_that_task_back_to_the_default()
    {
        var own = Provider("vendor-own", "answer from its own");
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review the change").WithLlm(own).Build();

        var (output, @default, _) = await RunBuiltAsync(agent, new MockLlmUsageSink(), new LlmConfigOverride { Profile = "default" });

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(1, @default.ChatCallCount);
        Assert.Equal(0, own.ChatCallCount);
    }

    [Fact]
    public void An_agent_runs_on_its_own_provider_or_on_a_profile_never_both()
    {
        var error = Assert.Throws<ArgumentException>(() => DomainAgent.Create(new AgentCreateOptions
        {
            Role = AgentRole.From("Reviewer"),
            Goal = AgentGoal.From("Review the change"),
            Llm = Provider("vendor-own", "x"),
            LlmConfig = LlmConfig.OnProfile("b"),
        }));

        Assert.Contains("vendor-own", error.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", error.Message, StringComparison.Ordinal);
    }
}
