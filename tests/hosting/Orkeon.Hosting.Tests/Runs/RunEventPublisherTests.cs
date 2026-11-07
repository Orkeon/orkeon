using Microsoft.Extensions.Logging;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests.Runs;

/// <summary>FWK-08, review: the early buffer of the publisher, which lets a history that joins late see the first events.</summary>
public sealed class RunEventPublisherTests
{
    private const int Capacity = 128;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ListSink : IRunEventSink
    {
        public List<RunEvent> Events { get; } = [];

        public System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
        {
            Events.Add(runEvent);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldReplayTheEarlyEventsInOrder_ToADestinationThatJoinsLate()
    {
        var run = new RunEventPublisher([], new ManualTimeProvider(), runId: "r");
        run.Emit(RunEventKinds.RunStarted);
        run.Emit(RunEventKinds.CostUpdated, new { tokens = 1 });
        var late = new ListSink();

        await run.AttachAsync(late, Ct);
        run.Emit(RunEventKinds.RunFinished);

        Assert.Equal([1, 2, 3], late.Events.Select(e => e.Seq));
        Assert.Equal(RunEventKinds.RunStarted, late.Events[0].Kind);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldLogHowManyEarlyEventsTheBufferCouldNotKeep_WhenALateDestinationJoins()
    {
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var run = new RunEventPublisher([], new ManualTimeProvider(), runId: "r", logger: factory.CreateLogger("publisher"));
        for (var i = 0; i < Capacity + 72; i++)
            run.Emit(RunEventKinds.CostUpdated, new { tokens = i });
        var late = new ListSink();

        await run.AttachAsync(late, Ct);

        Assert.Equal(Capacity, run.EarlyCount);
        Assert.Equal(72, run.DroppedEarly);
        Assert.Equal(Capacity, late.Events.Count);
        var warning = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("72", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldDropTheBuffer_WhenTheConfigurationIsCompleted()
    {
        var run = new RunEventPublisher([], new ManualTimeProvider(), runId: "r");
        run.Emit(RunEventKinds.RunStarted);

        run.CompleteConfiguration();
        run.Emit(RunEventKinds.CostUpdated);

        Assert.Equal(0, run.EarlyCount);
    }
}
