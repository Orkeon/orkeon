using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.Commands.Scripting.Streaming;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Streaming;

/// <summary>
/// A script learns whether the answer it holds already streamed inline to the console (#228): the
/// receipt the flow opened records whether the turn that produced the payload was the inline one, and
/// <see cref="CommandResponse.streamed"/> carries it.
/// </summary>
public sealed class InlineStreamReceiptTests
{
    [Fact]
    public async Task ShouldRecordStreamed_WhenTheForegroundTurnStreamsInline()
    {
        var sink = new ConsoleLlmDeltaSink(new ScriptedTestConsole());

        var receipt = await RunInOwnFlow(() => StreamTurn(sink, label: null, "answer"));

        Assert.True(receipt.Streamed);
    }

    [Fact]
    public async Task ShouldRecordNotStreamed_WhenTheTurnIsLabelled()
    {
        var sink = new ConsoleLlmDeltaSink(new ScriptedTestConsole());

        var receipt = await RunInOwnFlow(() => StreamTurn(sink, label: "t1", "answer"));

        Assert.False(receipt.Streamed);
    }

    [Fact]
    public async Task ShouldRecordNotStreamed_WhenTheTurnRunsUnderABackgroundTicket()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var instance = new CommandInstance("t2", "review", CommandInstanceKind.Async, "agent", "intent", Guid.NewGuid(), DateTimeOffset.UtcNow);

        var receipt = await RunInOwnFlow(() =>
        {
            ProgressAmbient.CurrentInstance = instance;
            StreamTurn(sink, label: null, "answer");
        });

        Assert.False(receipt.Streamed);
        Assert.Contains("[t2] answer", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRecordOnlyTheInlineTurn_WhenTwoUnlabelledTurnsStreamAtOnce()
    {
        var sink = new ConsoleLlmDeltaSink(new ScriptedTestConsole());
        var firstOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = RunInOwnFlowAsync(async () =>
        {
            var turn = sink.BeginTurn(null);
            turn.OnDelta("first ");
            firstOpened.SetResult();
            await secondDone.Task.ConfigureAwait(false);
            turn.OnDelta("answer");
            turn.OnTurnCompleted();
        });
        await firstOpened.Task;
        var second = await RunInOwnFlow(() => StreamTurn(sink, label: null, "second answer"));
        secondDone.SetResult();

        Assert.True((await first).Streamed);
        Assert.False(second.Streamed);
    }

    [Fact]
    public async Task ShouldRecordTheLastTurn_WhenTheFlowStreamsSeveralTurns()
    {
        var sink = new ConsoleLlmDeltaSink(new ScriptedTestConsole());

        var receipt = await RunInOwnFlow(() =>
        {
            StreamTurn(sink, label: null, "thinking");
            StreamTurn(sink, label: "t1", "answer");
        });

        Assert.False(receipt.Streamed);
    }

    [Fact]
    public async Task ShouldReportStreamed_WhenARequestedAgentStreamsItsAnswerInline()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var service = NewService(sink, out var registrations);
        using var _ = registrations;

        var response = await Task.Run(() => service.request("writer", "answer", "go"));

        Assert.True(response.success);
        Assert.Equal("the answer", response.payload);
        Assert.True(response.streamed);
        Assert.StartsWith("the answer", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldReportNotStreamed_WhenTheRequestedAgentDoesNotStream()
    {
        var service = NewService(sink: null, out var registrations);
        using var _ = registrations;

        var response = await Task.Run(() => service.request("writer", "answer", "go"));

        Assert.True(response.success);
        Assert.False(response.streamed);
    }

    [Fact]
    public async Task ShouldReportNotStreamed_WhenAPostedAgentWritesTicketPrefixedLines()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var service = NewService(sink, out var registrations);
        using var _ = registrations;

        var ticket = service.post("writer", "answer", "go");
        var result = await WaitForResult(service, ticket);

        Assert.Equal("the answer", result.payload);
        Assert.False(result.streamed);
        Assert.Contains($"[{ticket}] the answer", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNotBeStreamed_WhenAResponseIsBuiltWithoutAReceipt()
        => Assert.False(new CommandResponse("a", "i", success: true, payload: "p", error: null).streamed);

    private static void StreamTurn(ConsoleLlmDeltaSink sink, string? label, string text)
    {
        var turn = sink.BeginTurn(label);
        turn.OnDelta(text);
        turn.OnTurnCompleted();
    }

    /// <summary>Opens a receipt in a flow of its own, as the dispatch does, and runs <paramref name="body"/> in it.</summary>
    private static Task<InlineStreamReceipt> RunInOwnFlow(Action body)
        => RunInOwnFlowAsync(() =>
        {
            body();
            return Task.CompletedTask;
        });

    private static Task<InlineStreamReceipt> RunInOwnFlowAsync(Func<Task> body)
        => Task.Run(async () =>
        {
            var receipt = InlineStreamReceipt.Open();
            await body().ConfigureAwait(false);
            return receipt;
        });

    private static CommandDispatchService NewService(ConsoleLlmDeltaSink? sink, out CompositeRegistrations registrations)
    {
        var service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry());
        var writer = AgentId.Create();
        registrations = new CompositeRegistrations(
            service.Directory.Register("writer", writer),
            service.Channel.RegisterHandler(writer, async (req, _) =>
            {
                await Task.Yield();
                if (sink is not null)
                    StreamTurn(sink, label: null, "the answer");
                return AgentChannelResponse.Ok(req.CorrelationId, writer, "the answer");
            }));
        return service;
    }

    private static async Task<CommandResponse> WaitForResult(CommandDispatchService service, string ticket)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (service.get(ticket)?.result is { } result)
                return result;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"ticket {ticket} did not complete");
    }

    private sealed class CompositeRegistrations(params IDisposable[] parts) : IDisposable
    {
        public void Dispose()
        {
            foreach (var part in parts)
                part.Dispose();
        }
    }
}
