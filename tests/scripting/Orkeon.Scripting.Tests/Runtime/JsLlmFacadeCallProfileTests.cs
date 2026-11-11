using Jint;
using Jint.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Decision;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// #331: <c>{ llm: { profile } }</c> runs one <c>ctx.llm</c> call on a host profile, by the rule of
/// <c>llm.profile(name)</c>: an unknown name throws listing the known ones, <c>"default"</c> is the
/// host's provider, a decision profile applies its stages, and a <c>model</c> beside the profile
/// overrides the model within that profile's provider (as <c>llm.profile(name, { model })</c> does).
/// </summary>
public sealed class JsLlmFacadeCallProfileTests
{
    private sealed class Host
    {
        public StubLlmProvider Default { get; } = new() { Name = "host", BaseConfig = LlmConfig.Create("host-model") };
        public StubLlmProvider Agent { get; } = new() { Name = "agent", BaseConfig = LlmConfig.Create("agent-model") };
        public StubLlmProvider Small { get; } = new() { Name = "small", BaseConfig = LlmConfig.Create("small-model") };
        public StubDecisionStage Stage { get; } = new() { Name = "gate" };
        public Engine Engine { get; }
        public JsLlmFacade Facade { get; }

        public Host()
        {
            Default.RespondWith(new LlmResponse { Content = "from-host" }).RespondToChatWith(new LlmResponse { Content = "from-host" });
            Agent.RespondWith(new LlmResponse { Content = "from-agent" }).RespondToChatWith(new LlmResponse { Content = "from-agent" });
            Small.RespondWith(new LlmResponse { Content = "a" }).RespondToChatWith(new LlmResponse { Content = "from-small" });
            var guarded = new DecisionLlmProvider("guarded", Small, new DecisionStages([Stage], []),
                _ => Small, failClosed: true, NullLogger.Instance);
            var profiles = new LlmProfileRegistry(
                new ServiceCollection().BuildServiceProvider(),
                [new LlmProfileRegistration("small", _ => Small), new LlmProfileRegistration("guarded", _ => guarded)],
                null);
            Engine = new JsEngineFactory(llmProvider: Default, hostPorts: new ScriptingHostPorts { LlmProfiles = profiles }).Create();
            // The agent runs on its own provider: a profile named by the call replaces it for that call.
            Facade = new JsLlmFacade(Engine, Agent, CancellationToken.None);
        }

        public JsValue Options(string source) => Engine.Evaluate(source);
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldRunCompleteOnTheProfilesProvider_WhenTheCallNamesAProfile()
    {
        var host = new Host();

        var answer = await host.Facade.CompleteAsync("p", host.Options("({ llm: { profile: 'small' } })"));

        Assert.Equal("a", answer);
        Assert.Single(host.Small.GenerateCalls);
        Assert.Empty(host.Agent.GenerateCalls);
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldRunChatDecideAndActOnTheProfilesProvider_WhenTheCallNamesAProfile()
    {
        var host = new Host();
        var onSmall = host.Options("({ maxIterations: 1, llm: { profile: 'small' } })");

        await host.Facade.ChatAsync(host.Options("[{ role: 'user', content: 'hi' }]"), onSmall);
        var picked = await host.Facade.DecideAsync("pick", host.Options("['a', 'b']"), onSmall);
        await host.Facade.ActAsync(host.Engine, "do it", onSmall);

        Assert.Equal("a", picked);
        Assert.Equal(2, host.Small.ChatCalls.Count);
        Assert.Single(host.Small.GenerateCalls);
        Assert.Empty(host.Agent.ChatCalls);
        Assert.Empty(host.Agent.GenerateCalls);
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldOverrideTheModelWithinTheProfile_WhenTheCallNamesAProfileAndAModel()
    {
        var host = new Host();

        await host.Facade.CompleteAsync("p", host.Options("({ llm: { profile: 'small', model: 'small-xl' } })"));

        Assert.Single(host.Small.GenerateCalls);
        Assert.Equal("small-xl", host.Small.LastConfig!.Model);
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldRunOnTheHostsProvider_WhenTheCallNamesTheDefaultProfile()
    {
        var host = new Host();

        var answer = await host.Facade.CompleteAsync("p", host.Options("({ llm: { profile: 'default' } })"));

        Assert.Equal("from-host", answer);
        Assert.Empty(host.Agent.GenerateCalls);
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldThrowListingTheKnownProfiles_WhenTheCallNamesAnUnknownProfile()
    {
        var host = new Host();

        var error = await Assert.ThrowsAsync<InvalidScriptException>(
            () => host.Facade.CompleteAsync("p", host.Options("({ llm: { profile: 'huge' } })")));

        Assert.Contains("'huge'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, small, guarded.", error.Message, StringComparison.Ordinal);
        Assert.Empty(host.Agent.GenerateCalls);
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldThrow_WhenTheProfileIsNotAName()
    {
        var host = new Host();

        await Assert.ThrowsAsync<InvalidScriptException>(
            () => host.Facade.CompleteAsync("p", host.Options("({ llm: { profile: 42 } })")));
    }

    [Fact]
    [Trait("BR", "CH-331")]
    public async Task ShouldApplyTheStagesOfADecisionProfile_WhenTheCallNamesOne()
    {
        var host = new Host();
        host.Stage.Verdict = DecisionVerdict.Block("not on this profile");

        var blocked = await Assert.ThrowsAsync<DecisionBlockedException>(
            () => host.Facade.CompleteAsync("p", host.Options("({ llm: { profile: 'guarded' } })")));

        Assert.Equal("guarded", blocked.Profile);
        Assert.Equal(1, host.Stage.RequestCalls);
        Assert.Empty(host.Small.GenerateCalls);
    }
}
