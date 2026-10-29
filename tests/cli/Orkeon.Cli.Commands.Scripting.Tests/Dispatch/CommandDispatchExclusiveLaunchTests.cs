using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// #239: <c>postWork(..., exclusive: true)</c> refuses a target whose previous exclusive launch is not terminal, in the
/// same step as the registration of the instance.
/// </summary>
public sealed class CommandDispatchExclusiveLaunchTests
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static CommandDispatchService NewService() => new(
        new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
        new AgentCommandDirectory(),
        new CommandInstanceRegistry(),
        NullLogger<CommandDispatchService>.Instance);

    private async Task<CommandResponse> Gated(CancellationToken ct)
    {
        await _gate.Task.WaitAsync(ct).ConfigureAwait(false);
        return new CommandResponse("a", "i", success: true, payload: "p", error: null);
    }

    private static Task<CommandResponse> Instant(CancellationToken _)
        => Task.FromResult(new CommandResponse("a", "i", success: true, payload: "p", error: null));

    private static async Task WaitTerminal(CommandInstance instance)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!instance.IsTerminal && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(instance.IsTerminal, $"instance {instance.Ticket} never reached a terminal state.");
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldRefuseTheSecondLaunchWithTheFirstTicket_WhenTheFirstStillRuns()
    {
        var service = NewService();

        var first = service.postWork("review", "crew:review", Gated, exclusive: true);
        var refused = Assert.Throws<ExclusiveLaunchRefusedException>(() => service.postWork("review", "crew:review", Gated, exclusive: true));

        Assert.Equal(first, refused.Ticket);
        Assert.Equal($"crew:review already running (ticket {first})", refused.Message);
        Assert.Single(service.Registry.List(CommandInstanceFilter.All));
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(first)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldAdmitANewLaunch_WhenTheHolderCompleted()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Instant, exclusive: true);
        await WaitTerminal(service.Registry.Get(first)!);

        var second = service.postWork("review", "crew:review", Instant, exclusive: true);

        Assert.NotEqual(first, second);
        await WaitTerminal(service.Registry.Get(second)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldAdmitANewLaunch_WhenTheHolderWasCancelled()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Gated, exclusive: true);

        Assert.True(service.cancel(first));
        var second = service.postWork("review", "crew:review", Instant, exclusive: true);

        Assert.NotEqual(first, second);
        await WaitTerminal(service.Registry.Get(second)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldNeitherRefuseNorCount_ALaunchWithoutTheFlagOrOfAnotherTarget()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Gated, exclusive: true);

        var plain = service.postWork("review", "crew:review", Instant);
        var other = service.postWork("index", "crew:index", Instant, exclusive: true);

        Assert.Equal(3, service.Registry.List(CommandInstanceFilter.All).Count);
        await WaitTerminal(service.Registry.Get(plain)!);
        await WaitTerminal(service.Registry.Get(other)!);
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(first)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldAdmitExactlyOne_WhenLaunchesRace()
    {
        var service = NewService();
        using var start = new ManualResetEventSlim();
        var launches = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            try { return service.postWork("review", "crew:review", Gated, exclusive: true); }
            catch (ExclusiveLaunchRefusedException) { return null; }
        }, TestContext.Current.CancellationToken)).ToArray();

        start.Set();
        var tickets = await Task.WhenAll(launches);

        var admitted = Assert.Single(tickets, t => t is not null);
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(admitted!)!);
    }
}
