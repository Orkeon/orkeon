using System.Threading.Channels;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Crew;

namespace Orkeon.Infrastructure.Tests.Crew;

public sealed class CrewHookDispatcherCapFlagTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-25")]
    public async System.Threading.Tasks.Task ShouldCarryTheCapFlagIntoTheEvent(bool capped)
    {
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();
        var dispatcher = new CrewHookDispatcher(null);
        var snapshot = new TaskExecutionSnapshot
        {
            TaskId = "task-01",
            AgentRole = "agent",
            Success = true,
            Duration = TimeSpan.Zero,
            CompletedAt = DateTimeOffset.UtcNow,
            EndedOnIterationCap = capped,
        };

        using (CrewStreamScope.Begin(channel.Writer))
            await dispatcher.TaskCompletedAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.True(channel.Reader.TryRead(out var evt));
        Assert.Equal(capped, evt.EndedOnIterationCap);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldCarryTheMissingDeliverableIntoTheEvent(bool missing)
    {
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();
        var dispatcher = new CrewHookDispatcher(null);
        var snapshot = new TaskExecutionSnapshot
        {
            TaskId = "task-01",
            AgentRole = "agent",
            Success = true,
            Duration = TimeSpan.Zero,
            CompletedAt = DateTimeOffset.UtcNow,
            DeliverableMissing = missing,
        };

        using (CrewStreamScope.Begin(channel.Writer))
            await dispatcher.TaskCompletedAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.True(channel.Reader.TryRead(out var evt));
        Assert.Equal(missing, evt.DeliverableMissing);
    }
}
