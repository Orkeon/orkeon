using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#432: the window a loop compacts against - the profile's own figure for the profile's model alone.</summary>
public sealed class ContextCompactionWindowTests
{
    private const string ProfileModel = "big-model";

    public static TheoryData<string, string?, int?, int?> Cases => new()
    {
        { "an agent that names no model", null, null, 1_000_000 },
        { "an agent on the profile's model", ProfileModel, null, 1_000_000 },
        { "an agent on another model", "small-model", null, null },
        { "an agent that sets its own window", "small-model", 32_000, 32_000 },
    };

    [Theory]
    [Trait("BR", "CH-432")]
    [MemberData(nameof(Cases))]
    public void ShouldReadTheProfilesWindowForTheProfilesModelAlone_WhenTheLoopBeginsToCompact(
        string agentCase, string? agentModel, int? agentWindow, int? expectedWindow)
    {
        // Arrange: the profile declares a million tokens for its own model.
        _ = agentCase;
        var logger = new SpyExecutionLogger();
        var windows = new FakeContextWindowResolver(new ContextWindow(200_000, ContextWindowSources.Settings));
        var compaction = new ContextCompactionServices
        {
            Options = new ContextCompactionOptions(),
            Windows = windows,
            Logger = logger,
        }.For(LlmProfiles.Default, LlmConfig.OnProfile() with { Model = ProfileModel, ContextWindowTokens = 1_000_000 });
        var agent = new AgentBuilder().Role("Reader").Goal("Read the pages").Build();
        var callConfig = LlmConfig.OnProfile() with { Model = agentModel ?? string.Empty, ContextWindowTokens = agentWindow };

        // Act
        compaction.Begin(agent, "t1", callConfig, maxOutputTokens: null,
            (_, _, _) => System.Threading.Tasks.Task.FromResult(new CompactionSummary("s", 0, null)),
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()));

        // Assert
        var call = Assert.Single(windows.Calls);
        Assert.Equal(agentModel ?? ProfileModel, call.Model);
        Assert.Equal(expectedWindow, call.ProfileWindowTokens);
    }
}
