using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// GAP-34, decisions 2 and 4 — an agent's own provider (<see cref="DomainAgent.Llm"/>, renamed from
/// <c>FunctionCallingLlm</c>, which nothing read). An agent runs on its own provider or on a host
/// profile, never both; and a provider that runs its own tools — a Microsoft Agent Framework agent —
/// leaves the agent no Orkeon tool and no delegation: listed in its prompt, they would never be called.
/// </summary>
public sealed class AgentOwnProviderTests
{
    private sealed class StubProvider(string name, bool runsOwnTools) : ILlmProvider
    {
        public string Name { get; } = name;

        public LlmProviderCapabilities Capabilities { get; } =
            LlmProviderCapabilities.Unknown with { RunsOwnTools = runsOwnTools };

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });
    }

    private sealed class StubTool(string name) : IBaseTool
    {
        public string Name { get; } = name;

        public string Description => $"Stub tool {Name}";

        public ToolSchema Schema => new(Name, Description, []);

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "ok", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private static readonly StubProvider Bridge = new("agent-framework:Scripted", runsOwnTools: true);

    private static readonly StubProvider Model = new("vendor-c", runsOwnTools: false);

    private static AgentCreateOptions Options(ILlmProvider llm) => new()
    {
        Role = AgentRole.From("Reviewer"),
        Goal = AgentGoal.From("Review the change"),
        Llm = llm,
    };

    private static void AssertNamesTheRemedies(string message)
    {
        Assert.Contains("agent-framework:Scripted", message, StringComparison.Ordinal);
        Assert.Contains("Give the tool to the agent behind", message, StringComparison.Ordinal);
        Assert.Contains("WithAgentFrameworkTool", message, StringComparison.Ordinal);
    }

    // ── Decision 2: its own provider or a profile ──────────────────────────────────────────

    [Fact]
    public void Create_refuses_an_own_provider_with_a_host_profile_and_names_both()
    {
        var error = Assert.Throws<ArgumentException>(() => DomainAgent.Create(Options(Model).WithConfig(LlmConfig.OnProfile("b"))));

        Assert.Contains("vendor-c", error.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void Create_accepts_an_own_provider_with_a_configuration_naming_no_profile(string? profile)
    {
        var agent = DomainAgent.Create(Options(Model).WithConfig(LlmConfig.OnProfile(profile) with { Temperature = 0.2 }));

        Assert.Same(Model, agent.Llm);
        Assert.Equal(0.2, agent.LlmConfig!.Temperature, precision: 3);
    }

    [Fact]
    public void Build_refuses_an_own_provider_with_a_host_profile_in_the_builders_words()
    {
        var builder = new AgentBuilder().Role("Reviewer").Goal("Review the change")
            .WithLlm(Model).WithLlmConfig(LlmConfig.OnProfile("b"));

        var error = Assert.Throws<BuilderValidationException>(builder.Build);

        Assert.Equal("Agent", error.BuilderName);
        Assert.Contains(".WithLlm", error.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", error.Message, StringComparison.Ordinal);
        Assert.Contains("vendor-c", error.Message, StringComparison.Ordinal);
    }

    // ── Decision 4: no Orkeon tool on a provider that runs its own ─────────────────────────

    [Fact]
    public void Create_refuses_tools_on_a_provider_that_runs_its_own_naming_the_tool_and_the_remedies()
    {
        var error = Assert.Throws<ArgumentException>(() => DomainAgent.Create(Options(Bridge).WithTools(new StubTool("t"))));

        Assert.Contains("(t)", error.Message, StringComparison.Ordinal);
        AssertNamesTheRemedies(error.Message);
        // No delegation in sight: no word about switching it off.
        Assert.DoesNotContain("allowDelegation", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_refuses_delegation_on_a_provider_that_runs_its_own()
    {
        var error = Assert.Throws<ArgumentException>(() => DomainAgent.Create(Options(Bridge).WithDelegation()));

        Assert.Contains("delegate_work_to_coworker", error.Message, StringComparison.Ordinal);
        AssertNamesTheRemedies(error.Message);
        Assert.Contains("allowDelegation: false", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_keeps_tools_and_delegation_on_a_provider_that_calls_Orkeons()
    {
        var agent = DomainAgent.Create(Options(Model).WithTools(new StubTool("t")).WithDelegation());

        Assert.Single(agent.Tools);
        Assert.True(agent.AllowDelegation);
    }

    [Fact]
    public void Build_refuses_tools_and_delegation_on_a_provider_that_runs_its_own()
    {
        var withTool = new AgentBuilder().Role("Reviewer").Goal("Review").WithLlm(Bridge).WithTool(new StubTool("t"));
        var delegating = new AgentBuilder().Role("Reviewer").Goal("Review").WithLlm(Bridge).AllowDelegation();

        var toolError = Assert.Throws<BuilderValidationException>(withTool.Build);
        var delegationError = Assert.Throws<BuilderValidationException>(delegating.Build);

        Assert.Contains("(t)", toolError.Message, StringComparison.Ordinal);
        AssertNamesTheRemedies(toolError.Message);
        Assert.Contains("ask_question_to_coworker", delegationError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTool_keeps_the_rule_on_an_agent_whose_provider_runs_its_own_tools()
    {
        var agent = DomainAgent.Create(Options(Bridge));

        var error = Assert.Throws<InvalidOperationException>(() => agent.AddTool(new StubTool("late")));

        Assert.Contains("(late)", error.Message, StringComparison.Ordinal);
        AssertNamesTheRemedies(error.Message);
        Assert.Empty(agent.Tools);
    }

    [Fact]
    public void UpdateConfiguration_refuses_to_switch_delegation_on_for_such_an_agent()
    {
        var agent = DomainAgent.Create(Options(Bridge));

        var error = Assert.Throws<InvalidOperationException>(() => agent.UpdateConfiguration(allowDelegation: true));

        Assert.Contains("delegate_work_to_coworker", error.Message, StringComparison.Ordinal);
        Assert.False(agent.AllowDelegation);
        // Switching it off, or changing anything else, stays allowed.
        agent.UpdateConfiguration(allowDelegation: false, maxIterations: 3);
        Assert.Equal(3, agent.MaxIterations);
    }

    [Fact]
    public void Restore_keeps_the_agents_own_provider()
    {
        var restored = DomainAgent.Restore(new AgentSnapshot
        {
            Id = AgentId.Create(),
            Role = AgentRole.From("Reviewer"),
            Goal = AgentGoal.From("Review"),
            Status = AgentStatus.Idle,
            MaxIterations = 3,
            MaxRpm = 10,
            MaxRetryLimit = 1,
            Llm = Bridge,
        });

        Assert.Same(Bridge, restored.Llm);
    }
}

/// <summary>Option helpers for <see cref="AgentOwnProviderTests"/>: <see cref="AgentCreateOptions"/> is a class.</summary>
internal static class AgentCreateOptionsTestExtensions
{
    public static AgentCreateOptions WithConfig(this AgentCreateOptions options, LlmConfig config) => Copy(options, llmConfig: config);

    public static AgentCreateOptions WithTools(this AgentCreateOptions options, params IBaseTool[] tools) => Copy(options, tools: tools);

    public static AgentCreateOptions WithDelegation(this AgentCreateOptions options) => Copy(options, allowDelegation: true);

    private static AgentCreateOptions Copy(
        AgentCreateOptions options, LlmConfig? llmConfig = null, IEnumerable<IBaseTool>? tools = null, bool? allowDelegation = null) => new()
    {
        Role = options.Role,
        Goal = options.Goal,
        Llm = options.Llm,
        LlmConfig = llmConfig ?? options.LlmConfig,
        Tools = tools ?? options.Tools,
        AllowDelegation = allowDelegation ?? options.AllowDelegation,
    };
}
