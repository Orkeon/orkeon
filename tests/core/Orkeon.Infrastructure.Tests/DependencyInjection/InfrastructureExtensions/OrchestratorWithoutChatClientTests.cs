using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// GAP-32: <c>AddOrkeonInfrastructure()</c> registered a streaming agent service by default, which
/// required an <see cref="IChatClient"/>; the orchestrator took it as a constructor parameter, so a
/// container without a chat client could not resolve the orchestrator at all — though the execution
/// runs without one (the native and text loops). The service is gone: streaming is the crew's own
/// run, and a host without a chat client kicks its crews off, streamed or not.
/// </summary>
public class OrchestratorWithoutChatClientTests
{
    private const string Crew = """
        name: launch-desk
        goal: Publish the launch notes
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the launch notes
        tasks:
          draft:
            description: Draft the launch notes
            expected_output: The draft
            agent: writer
        """;

    [Fact]
    public async Task A_container_without_a_chat_client_resolves_the_orchestrator_and_runs_a_crew_streamed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        // A model on the basic surfaces only: no IChatClient anywhere.
        services.AddSingleton<IBasicLlmProvider>(new LlmProviderAdapter(new MockLlmProvider { Name = "vendor" }));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        Assert.Null(sp.GetService<IChatClient>());

        var orchestrator = Assert.IsType<SequentialCrewOrchestrator>(sp.GetRequiredService<ICrewOrchestrationService>());
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(Crew, TestContext.Current.CancellationToken);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, TestContext.Current.CancellationToken);
        var events = new List<CrewExecutionEvent>();
        await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, CrewInput.Empty("launch"), TestContext.Current.CancellationToken))
            events.Add(executionEvent);

        // Without a chat client the turns are not streamed — no delta —, every other event is there.
        Assert.Equal(
            [Orkeon.Constants.Protocol.RunEventKinds.TaskStarted, Orkeon.Constants.Protocol.RunEventKinds.TaskCompleted, Orkeon.Constants.Protocol.RunEventKinds.RunFinished],
            events.Select(e => e.Kind));
        var output = events[^1].Output;
        Assert.NotNull(output);
        Assert.True(output.Succeeded, output.Error);
    }
}
