using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// #429, #433 — a <c>decision.mode: required</c> crew on a host whose container registers no decision model, through
/// the real wiring: the run is refused with <c>decision_unavailable</c>, and its <c>run.decision</c> notice names the
/// circuit the run follows, off by <see cref="DecisionCircuitCause.NoProvider"/>, even when the host registers the
/// circuit on itself.
/// </summary>
public sealed class RequiredDecisionWiringTests
{
    private const string RequiredCrew = """
        name: required
        goal: Write the notes
        process: sequential
        decision:
          mode: required
        agents:
          writer:
            role: Writer
            goal: Write
        tasks:
          notes:
            description: Write the notes
            expectedOutput: Notes
            agent: writer
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Container(MockCrewExecutionHook hook, MockLlmProvider provider, bool hostRegistersTheCircuitOn)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddSingleton<ICrewExecutionHook>(hook);
        if (hostRegistersTheCircuitOn)
            services.AddSingleton(DecisionCircuit.On);
        services.AddOrkeonLlmProvider(_ => provider, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheRunAndNameNoProvider_WhenTheContainerRegistersNoDecisionModel(bool hostRegistersTheCircuitOn)
    {
        // Arrange
        var hook = new MockCrewExecutionHook();
        var provider = new MockLlmProvider { Name = "vendor-default" };
        provider.SetChatResult(new LlmResponse { Content = "notes" });
        await using var container = Container(hook, provider, hostRegistersTheCircuitOn);
        await using var scope = container.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(RequiredCrew, Ct);
        var crew = await scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);

        // Act
        var output = await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("notes", new Dictionary<string, object>()), Ct);

        // Assert
        Assert.Equal((false, Orkeon.Constants.Protocol.RunEventErrorCodes.DecisionUnavailable), (output.Succeeded, output.ErrorCode));
        var notice = Assert.Single(hook.Decisions);
        Assert.Equal((false, DecisionCircuitCause.NoProvider), (notice.Circuit.Enabled, notice.Circuit.Cause));
        Assert.Equal(0, provider.ChatCallCount);
    }
}
