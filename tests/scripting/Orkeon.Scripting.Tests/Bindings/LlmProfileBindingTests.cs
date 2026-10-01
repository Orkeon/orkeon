using Jint;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Bindings;

/// <summary>
/// GAP-17, the <c>.ork.ts</c> half: <c>llm.profile(name, overrides?)</c> picks one of the host's
/// named profiles. The configuration carries the profile to the crew (declarative shape), and an
/// agent configured with it asks the profile's provider through <c>ctx.llm</c> (procedural shape).
/// A name the host does not offer throws at the call, listing the known ones.
/// </summary>
public sealed class LlmProfileBindingTests
{
    private sealed class StubLlmProvider(string name, string? model) : ILlmProvider
    {
        public string Name { get; } = name;
        public LlmConfig? BaseConfig { get; } = model is null ? null : LlmConfig.Create(model);
        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = Name });
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = Name });
    }

    private static readonly StubLlmProvider s_host = new("openai", "gpt-4o-mini");
    private static readonly StubLlmProvider s_claude = new("anthropic", "claude-sonnet-5");

    private static LlmProfileRegistry Profiles(params string[] allowed)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new LlmProfileRegistry(
            services,
            [new LlmProfileRegistration("claude", _ => s_claude), new LlmProfileRegistration("local", _ => new StubLlmProvider("ollama", "qwen3"))],
            allowed.Length == 0 ? null : Microsoft.Extensions.Options.Options.Create(new LlmProfileAccessOptions { AllowedProfiles = allowed }));
    }

    private static Engine Engine(ILlmProfileRegistry? profiles) =>
        new JsEngineFactory(llmProvider: s_host, hostPorts: new ScriptingHostPorts { LlmProfiles = profiles }).Create();

    [Fact]
    public void llm_profile_is_the_profiles_provider_on_its_own_model()
    {
        var config = (JsLlmConfig)Engine(Profiles()).Evaluate("llm.profile('claude')").ToObject()!;

        Assert.Equal("anthropic", config.provider);
        Assert.Equal("claude", config.profile);
        Assert.Equal("claude-sonnet-5", config.model);
    }

    [Fact]
    public void llm_profile_applies_its_overrides_and_keeps_the_profile()
    {
        var config = (JsLlmConfig)Engine(Profiles())
            .Evaluate("llm.profile('claude', { model: 'claude-haiku-5', temperature: 0.1 }).with({ maxTokens: 500 })")
            .ToObject()!;

        Assert.Equal("claude", config.profile);
        Assert.Equal("claude-haiku-5", config.model);
        Assert.Equal(0.1, config.temperature);
        Assert.Equal(500, config.maxTokens);
    }

    [Fact]
    public void llm_profile_default_is_llm_default()
    {
        var config = (JsLlmConfig)Engine(Profiles()).Evaluate("llm.profile('default')").ToObject()!;

        Assert.Equal("openai", config.provider);
        Assert.Null(config.profile);
    }

    [Fact]
    public void An_unknown_profile_throws_and_lists_the_known_ones()
    {
        var error = Assert.Throws<InvalidScriptException>(() => Engine(Profiles()).Evaluate("llm.profile('gpt')"));

        Assert.Contains("'gpt'", error.Message, StringComparison.Ordinal);
        Assert.Contains("default, claude, local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_without_profiles_offers_the_default_alone()
    {
        var error = Assert.Throws<InvalidScriptException>(() => Engine(null).Evaluate("llm.profile('claude')"));

        Assert.Contains("Known profiles: default.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_the_host_allow_list_leaves_out_is_unknown_to_the_script()
    {
        var error = Assert.Throws<InvalidScriptException>(() => Engine(Profiles("local")).Evaluate("llm.profile('claude')"));

        Assert.Contains("default, local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_declarative_shape_carries_the_profile_to_the_crew_configuration()
    {
        var crew = (JsCrew)Engine(Profiles()).Evaluate("""
            const planner = agentBuilder().name("planner").role("Planner").goal("Plan")
                .llm(llm.profile("claude")).build();
            const writer = agentBuilder().name("writer").role("Writer").goal("Write").build();
            const plan = taskBuilder().name("plan").agent(planner).description("Plan").expectedOutput("A plan").build();
            const write = taskBuilder().name("write").agent(writer).description("Write").expectedOutput("Text").build();
            crewBuilder().name("mixed").goal("g").withAgent(planner).withAgent(writer).withTask(plan).withTask(write).build();
            """).ToObject()!;

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var planner = Assert.Single(config.Agents, a => a.Role == "Planner");
        Assert.Equal("claude", planner.LlmConfig!.Profile);
        Assert.Equal("claude-sonnet-5", planner.LlmConfig.Model);
        Assert.Null(Assert.Single(config.Agents, a => a.Role == "Writer").LlmConfig);
    }

    [Fact]
    public void A_response_format_alone_does_not_pin_the_framework_default_model()
    {
        var crew = (JsCrew)Engine(Profiles()).Evaluate("""
            const a = agentBuilder().name("a").role("A").goal("g").withResponseFormat("json_object").build();
            const t = taskBuilder().name("t").agent(a).description("d").expectedOutput("e").build();
            crewBuilder().name("c").goal("g").withAgent(a).withTask(t).build();
            """).ToObject()!;

        var agent = Assert.Single(JsCrewConfigurationAdapter.ToConfiguration(crew).Agents);

        // Empty: the host's own model — the framework default would be sent to any vendor.
        Assert.Equal(string.Empty, agent.LlmConfig!.Model);
        Assert.Equal("json_object", agent.LlmConfig.ResponseFormat!.Type);
    }

    [Fact]
    public void A_procedural_agent_on_a_profile_asks_its_providers_ctx_llm()
    {
        var crew = (JsCrew)Engine(Profiles()).Evaluate("""
            const onClaude = agentBuilder().name("c").role("C").goal("g").llm(llm.profile("claude")).build();
            const onHost = agentBuilder().name("h").role("H").goal("g").build();
            crewBuilder().name("p").goal("g").withAgent(onClaude).withAgent(onHost).build();
            """).ToObject()!;
        var agents = crew.SnapshotAgents();

        var claudeEnvironment = crew.CreateEnvironment(agents.Single(a => a.name == "c"), null, CancellationToken.None);
        var hostEnvironment = crew.CreateEnvironment(agents.Single(a => a.name == "h"), null, CancellationToken.None);

        Assert.Equal("anthropic", claudeEnvironment.LlmProvider!.Name);
        Assert.Same(s_host, hostEnvironment.LlmProvider);
    }
}
