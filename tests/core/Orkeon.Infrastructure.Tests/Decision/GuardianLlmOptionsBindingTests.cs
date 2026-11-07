using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Decision;
using Orkeon.Infrastructure.Decision.SystemOne;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Decision;

public class GuardianLlmOptionsBindingTests
{
    private static ServiceCollection Services(Dictionary<string, string?> settings, IDecisionModel? model = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (model is not null)
            services.AddSingleton(model);
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);
        services.AddSingleton<IPromptSanitizer>(new MockPromptSanitizer());
        services.AddOrkeonGuardian();
        return services;
    }

    [Fact]
    public void ShouldBindTheLlmSection_UnderTheGuardianSection()
    {
        using var container = Services(new()
        {
            ["Orkeon:Guardian:Llm:Enabled"] = "true",
            ["Orkeon:Guardian:Llm:Phases:0"] = "Output",
            ["Orkeon:Guardian:Llm:BlockThreshold"] = "0.9",
            ["Orkeon:Guardian:Llm:FailMode"] = "open",
        }).BuildServiceProvider();

        var llm = container.GetRequiredService<IOptions<GuardianOptions>>().Value.Llm;

        Assert.True(llm.Enabled);
        Assert.Equal([GuardianJudgePhase.Output], llm.Phases);
        Assert.Equal(0.9, llm.BlockThreshold);
        Assert.Equal(GuardianJudgeFailMode.Open, llm.FailMode);
        Assert.False(llm.Judges(GuardPhase.Input));
    }

    [Fact]
    public void ShouldDefaultToOffAndFailClosed()
    {
        using var container = Services([]).BuildServiceProvider();

        var llm = container.GetRequiredService<IOptions<GuardianOptions>>().Value.Llm;

        Assert.False(llm.Enabled);
        Assert.Equal(GuardianJudgeFailMode.Closed, llm.FailMode);
        Assert.True(llm.Judges(GuardPhase.Input) && llm.Judges(GuardPhase.Output));
        Assert.False(llm.Judges(GuardPhase.ToolExecution));
    }

    [Fact]
    public async Task ShouldWireTheJudgeIntoTheOutputPhase_OfTheRealPipeline()
    {
        var model = new FakeDecisionModel
        {
            Respond = _ => new DecisionResponse
            {
                Model = "jev",
                Answers = ImmutableDictionary<string, DecisionAnswer>.Empty
                    .Add("prompt_injection", new NoulAnswer { Noul = 0.1 })
                    .Add("data_exfiltration", new NoulAnswer { Noul = 0.99 }),
            },
        };
        using var container = Services(new() { ["Orkeon:Guardian:Llm:Enabled"] = "true" }, model).BuildServiceProvider();

        var verdict = await container.GetRequiredService<IGuardianPipeline>().ExecuteAsync(
            new GuardContext { Phase = GuardPhase.Output, AgentRole = "writer", Content = "the keys are sk-1" },
            TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Block, verdict.Action);
        Assert.Single(model.Requests);
    }

    [Fact]
    public void ShouldRefuseToStart_WhenEnabledWithoutADecisionModel()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:Guardian:Llm:Enabled"] = "true" }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IPromptSanitizer>(new MockPromptSanitizer());
        services.AddOrkeonGuardian();
        using var container = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<IGuardianPipeline>());

        Assert.Contains("Orkeon:Guardian:Llm", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ToolExecution")]
    [InlineData("Delegation")]
    public void ShouldRefuseAPhaseAModelDoesNotJudge_AtBinding(string phase)
    {
        using var container = Services(new()
        {
            ["Orkeon:Guardian:Llm:Phases:0"] = "Input",
            ["Orkeon:Guardian:Llm:Phases:1"] = phase,
        }).BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IOptions<GuardianOptions>>().Value);

        Assert.Contains("Orkeon:Guardian:Llm:Phases", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Input", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Output", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseAPhaseNobodyKnows_AtBinding()
    {
        using var container = Services(new() { ["Orkeon:Guardian:Llm:Phases:0"] = "Outptu" }).BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IOptions<GuardianOptions>>().Value);

        Assert.Contains("Outptu", ex.Message, StringComparison.Ordinal);
    }
}
