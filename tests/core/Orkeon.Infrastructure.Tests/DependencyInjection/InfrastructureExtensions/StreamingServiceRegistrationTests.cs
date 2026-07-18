using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Guards the P2-O-03 guarantee: <see cref="AddOrkeonInfrastructure"/> registers a concrete
/// <see cref="IStreamingAgentExecutionService"/> by default, so a host gets AgentThought-level
/// (tool-call granular) streaming from <c>KickoffStreamingAsync</c> rather than the silent
/// per-task-replay fallback. Asserted at the descriptor level so the test needs no LLM/IChatClient.
/// </summary>
public class StreamingServiceRegistrationTests
{
    [Fact]
    public void ShouldRegisterStreamingService_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOrkeonInfrastructure();

        var descriptor = Assert.Single(
            services,
            d => d.ServiceType == typeof(IStreamingAgentExecutionService));
        Assert.Equal(typeof(StreamingAgentExecutionService), descriptor.ImplementationType);
    }
}
