using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Interop.AgentFramework.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Interop.AgentFramework.Tests;

/// <summary>
/// GAP-34 — an Orkeon agent built with <c>WithAgentFrameworkAgent</c> answers through the MAF agent:
/// its turns go to the MAF agent, on a client the run builds over the agent's own provider and meters
/// as the agent's work. The bridge set a field nothing read: the host's default model answered every
/// task, with the agent's Orkeon tools, and the MAF agent heard nothing. Orkeon tools, which a MAF agent
/// can never call, are refused at build and when a task falls on a provider that runs its own.
/// </summary>
public sealed class AIAgentAnswersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StubLlmProvider Default() =>
        new StubLlmProvider { Name = "host-default" }.RespondToChatWith(new LlmResponse { Content = "the default answered" });

    /// <summary>A host like the runners': the application, the infrastructure, a usage sink, a default model.</summary>
    private static ServiceProvider Host(MockLlmUsageSink sink, Action<IServiceCollection> models)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddSingleton<ILlmUsageSink>(sink);
        models(services);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static CrewTaskBuilder ReviewTask(DomainAgent agent) =>
        new CrewTaskBuilder().Description("Review the CI change").ExpectedOutput("The biggest risk, in one sentence").AssignTo(agent);

    /// <summary>Saves the agent, the task and a one-task crew in a scope, then kicks the crew off.</summary>
    private static async Task<CrewOutput> RunAsync(ServiceProvider container, DomainAgent agent, CrewTask task)
    {
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var crew = new CrewBuilder().Goal("Review the change").Sequential().WithAgent(agent).WithTask(task).Build();
        await sp.GetRequiredService<IAgentRepository>().AddAsync(agent, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(task, Ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);
        return await sp.GetRequiredService<Orkeon.Application.Interfaces.Services.ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("maf", new Dictionary<string, object>()), Ct);
    }

    private static void AssertNamesTheRemedies(string? message, string tool)
    {
        Assert.NotNull(message);
        Assert.Contains($"{tool}", message, StringComparison.Ordinal);
        Assert.Contains("agent-framework:Scripted", message, StringComparison.Ordinal);
        Assert.Contains("Give the tool to the agent behind", message, StringComparison.Ordinal);
        Assert.Contains("WithAgentFrameworkTool", message, StringComparison.Ordinal);
    }

    // ── The MAF agent answers ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_MAF_agent_answers_the_task_of_the_Orkeon_agent_it_backs_counted_once_as_its_work()
    {
        var scripted = new ScriptedAIAgent();
        var sink = new MockLlmUsageSink();
        var @default = Default();
        await using var container = Host(sink, services => services.AddOrkeonLlmProvider(_ => @default));
        var reviewer = new AgentBuilder().Role("Reviewer").Goal("Find the biggest risk of a change")
            .WithAgentFrameworkAgent(scripted).Build();
        var task = ReviewTask(reviewer).Build();

        var output = await RunAsync(container, reviewer, task);

        Assert.True(output.Succeeded, output.Error);
        var run = Assert.Single(scripted.Runs);
        var system = Assert.Single(run.Messages, m => m.Role == ChatRole.System);
        Assert.Contains("You are Reviewer", system.Text, StringComparison.Ordinal);
        Assert.Contains("Find the biggest risk of a change", system.Text, StringComparison.Ordinal);
        var user = Assert.Single(run.Messages, m => m.Role == ChatRole.User);
        Assert.Contains("Review the CI change", user.Text, StringComparison.Ordinal);
        Assert.Contains("The biggest risk, in one sentence", user.Text, StringComparison.Ordinal);
        Assert.StartsWith("scripted reply <- ", output.FinalOutput, StringComparison.Ordinal);
        Assert.Contains("Review the CI change", output.FinalOutput, StringComparison.Ordinal);
        Assert.Empty(@default.ChatCalls);
        Assert.Empty(@default.GenerateCalls);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal("agent-framework:Scripted", usage.Provider);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal("Reviewer", usage.AgentId);
        Assert.Equal(task.Id.ToString(), usage.TaskId);
    }

    [Fact]
    public async Task A_MAF_agent_over_Orkeons_own_model_is_counted_once_under_that_model()
    {
        // The example builds its reviewer over the host's model, metered already: the meter nearest
        // the model counts, and the one the run puts on the bridge stays silent.
        var model = new StubLlmProvider { Name = "vendor-model" }.RespondToChatWith(new LlmResponse
        {
            Content = "The risk is a flaky runner.",
            Model = "model-x",
            PromptTokens = 20,
            CompletionTokens = 5,
            TokensUsed = 25,
        });
        var sink = new MockLlmUsageSink();
        await using var container = Host(sink, services => services.AddOrkeonLlmProvider(_ => model));
        var maf = new ProviderBackedAIAgent("Reviewer", container.GetRequiredService<ILlmProvider>());
        var reviewer = new AgentBuilder().Role("Reviewer").Goal("Find the biggest risk of a change")
            .WithAgentFrameworkAgent(maf).Build();

        var output = await RunAsync(container, reviewer, ReviewTask(reviewer).Build());

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(1, maf.Runs);
        Assert.Equal("The risk is a flaky runner.", output.FinalOutput);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal("vendor-model", usage.Provider);
        Assert.Equal("model-x", usage.Model);
        Assert.Equal(20, usage.PromptTokens);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal("Reviewer", usage.AgentId);
    }

    [Fact]
    public async Task A_turn_whose_agent_and_task_declare_options_warns_about_each_one_the_bridge_does_not_send()
    {
        // The agent's temperature and the task's response format reach the bridge through the agent
        // loop, and stop there: each is said once, never dropped in silence (decision 7).
        var scripted = new ScriptedAIAgent { Respond = _ => """{"risk": "a flaky runner"}""" };
        var logger = new StructuredLogger<AIAgentLlmProvider>();
        await using var container = Host(new MockLlmUsageSink(), services => services.AddOrkeonLlmProvider(_ => Default()));
        var reviewer = new AgentBuilder().Role("Reviewer").Goal("Find the biggest risk of a change")
            .WithAgentFrameworkAgent(scripted, logger)
            .WithLlmConfig(LlmConfig.OnProfile() with { Temperature = 0.2 })
            .Build();
        var task = ReviewTask(reviewer).WithResponseFormat(LlmResponseFormat.JsonObject()).Build();

        var output = await RunAsync(container, reviewer, task);

        Assert.True(output.Succeeded, output.Error);
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        Assert.Equal(["response_format", "temperature"], warnings.Select(w => (string?)w.Fields["Option"]).Order(StringComparer.Ordinal));
        Assert.All(warnings, warning => Assert.Equal("agent-framework:Scripted", warning.Fields["ProviderName"]));
    }

    // ── Orkeon tools: refused, never offered in silence ───────────────────────────────────

    [Fact]
    public void Build_refuses_an_Orkeon_tool_or_delegation_on_an_agent_the_MAF_agent_answers_for()
    {
        var withTool = new AgentBuilder().Role("Reviewer").Goal("Review")
            .WithAgentFrameworkAgent(new ScriptedAIAgent()).WithTool(new StubBaseTool("t"));
        var delegating = new AgentBuilder().Role("Reviewer").Goal("Review")
            .WithAgentFrameworkAgent(new ScriptedAIAgent()).AllowDelegation();

        AssertNamesTheRemedies(Assert.Throws<BuilderValidationException>(withTool.Build).Message, "(t)");
        AssertNamesTheRemedies(Assert.Throws<BuilderValidationException>(delegating.Build).Message, "delegate_work_to_coworker");
    }

    [Fact]
    public void AddTool_refuses_an_Orkeon_tool_on_such_an_agent()
    {
        var reviewer = new AgentBuilder().Role("Reviewer").Goal("Review").WithAgentFrameworkAgent(new ScriptedAIAgent()).Build();

        AssertNamesTheRemedies(Assert.Throws<InvalidOperationException>(() => reviewer.AddTool(new StubBaseTool("t"))).Message, "(t)");
    }

    [Fact]
    public async Task A_task_carrying_a_tool_fails_before_the_MAF_agent_runs()
    {
        var scripted = new ScriptedAIAgent();
        var sink = new MockLlmUsageSink();
        await using var container = Host(sink, services => services.AddOrkeonLlmProvider(_ => Default()));
        var reviewer = new AgentBuilder().Role("Reviewer").Goal("Review").WithAgentFrameworkAgent(scripted).Build();

        var output = await RunAsync(container, reviewer, ReviewTask(reviewer).WithTool(new StubBaseTool("t")).Build());

        Assert.False(output.Succeeded);
        AssertNamesTheRemedies(output.Error, "(t)");
        Assert.Empty(scripted.Runs);
        Assert.Empty(sink.Recorded);
    }

    [Fact]
    public async Task A_task_asking_for_human_input_fails_too_on_a_host_that_registers_the_tool()
    {
        var scripted = new ScriptedAIAgent();
        await using var container = Host(new MockLlmUsageSink(), services =>
        {
            services.AddOrkeonLlmProvider(_ => Default());
            services.AddOrkeonHumanInput();
        });
        var reviewer = new AgentBuilder().Role("Reviewer").Goal("Review").WithAgentFrameworkAgent(scripted).Build();

        var output = await RunAsync(container, reviewer, ReviewTask(reviewer).HumanInput().Build());

        Assert.False(output.Succeeded);
        AssertNamesTheRemedies(output.Error, "human_input");
        Assert.Empty(scripted.Runs);
    }

    [Fact]
    public async Task A_MAF_agent_registered_as_a_host_profile_refuses_the_tools_of_a_task_that_falls_on_it()
    {
        var scripted = new ScriptedAIAgent();
        await using var container = Host(new MockLlmUsageSink(), services =>
        {
            services.AddOrkeonLlmProvider(_ => Default());
            services.AddOrkeonLlmProfile("maf", _ => new AIAgentLlmProvider(scripted));
        });
        var worker = new AgentBuilder().Role("Worker").Goal("Work")
            .WithLlmConfig(LlmConfig.OnProfile("maf")).WithTool(new StubBaseTool("t")).Build();

        var output = await RunAsync(container, worker, ReviewTask(worker).Build());

        Assert.False(output.Succeeded);
        AssertNamesTheRemedies(output.Error, "(t)");
        Assert.Empty(scripted.Runs);
    }

    [Fact]
    public async Task An_agent_allowing_delegation_on_a_MAF_profile_is_told_to_switch_it_off()
    {
        // A YAML agent allows delegation unless it says otherwise: on a profile a MAF agent answers
        // for, its delegation tools are refused like any other, and the message says how to drop them.
        var scripted = new ScriptedAIAgent();
        await using var container = Host(new MockLlmUsageSink(), services =>
        {
            services.AddOrkeonLlmProvider(_ => Default());
            services.AddOrkeonLlmProfile("maf", _ => new AIAgentLlmProvider(scripted));
        });
        var worker = new AgentBuilder().Role("Worker").Goal("Work")
            .WithLlmConfig(LlmConfig.OnProfile("maf")).AllowDelegation().Build();

        var output = await RunAsync(container, worker, ReviewTask(worker).Build());

        Assert.False(output.Succeeded);
        AssertNamesTheRemedies(output.Error, "delegate_work_to_coworker");
        Assert.Contains("allowDelegation: false", output.Error, StringComparison.Ordinal);
        Assert.Empty(scripted.Runs);
    }

    [Fact]
    public async Task A_MAF_agent_registered_as_the_hosts_default_refuses_them_too()
    {
        var scripted = new ScriptedAIAgent();
        await using var container = Host(new MockLlmUsageSink(), services =>
            services.AddOrkeonLlmProvider(_ => new AIAgentLlmProvider(scripted)));
        var worker = new AgentBuilder().Role("Worker").Goal("Work").WithTool(new StubBaseTool("t")).Build();

        var output = await RunAsync(container, worker, ReviewTask(worker).Build());

        Assert.False(output.Succeeded);
        AssertNamesTheRemedies(output.Error, "(t)");
        Assert.Empty(scripted.Runs);
    }

    [Fact]
    public async Task A_MAF_agent_without_any_tool_on_the_hosts_default_answers()
    {
        var scripted = new ScriptedAIAgent();
        await using var container = Host(new MockLlmUsageSink(), services =>
            services.AddOrkeonLlmProvider(_ => new AIAgentLlmProvider(scripted)));
        var worker = new AgentBuilder().Role("Worker").Goal("Work").Build();

        var output = await RunAsync(container, worker, ReviewTask(worker).Build());

        Assert.True(output.Succeeded, output.Error);
        Assert.Single(scripted.Runs);
    }
}
