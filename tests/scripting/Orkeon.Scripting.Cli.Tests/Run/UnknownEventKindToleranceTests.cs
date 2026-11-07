using Orkeon.Scripting.Cli.Commands.Run;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// The inbound pump reads lines of a run's stdin: a line whose kind it does not know, an
/// <c>llm.fallback</c> (FWK-05) or a <c>graph.node.entered</c> (FWK-27) echoed back included, is
/// not an answer and not an error. One class for every kind a lot adds.
/// </summary>
public sealed class UnknownEventKindToleranceTests
{
    [Fact]
    public async Task A_line_of_a_kind_the_pump_does_not_handle_is_not_taken_for_an_answer()
    {
        var reader = new FeedableReader();
        var commands = new List<string>();
        await using var pump = new InboundCommandPump(reader, (line, _) =>
        {
            lock (commands)
                commands.Add(line);
            return Task.CompletedTask;
        });
        var waiter = pump.ReadAnswerAsync("q1", TestContext.Current.CancellationToken);

        reader.Feed($$"""{"kind":"{{RunEventKinds.LlmFallback}}","profile":"main","from":"a","to":"b","reason":"server_error"}""");
        reader.Feed("""{"kind":"input.given","value":"oui"}""");

        Assert.Equal("oui", await waiter.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"kind":"graph.node.entered","node":"first","visit":1}""")]
    [InlineData("""{"kind":"kind.from.the.future"}""")]
    public async Task ShouldKeepReadingAndAnswerTheNextQuestion_WhenALineOfAnUnknownKindArrives(string unknown)
    {
        var reader = new FeedableReader();
        await using var pump = new InboundCommandPump(reader, (_, _) => Task.CompletedTask);

        reader.Feed(unknown);
        reader.Feed("""{"kind":"input.given","correlationId":"q1","value":"yes"}""");
        var answer = await pump.ReadAnswerAsync("q1", TestContext.Current.CancellationToken);

        Assert.NotNull(answer);
        Assert.Contains("yes", answer, StringComparison.Ordinal);
        reader.Close_();
    }

    [Fact]
    public void The_new_kinds_are_declared_kinds_of_the_protocol()
    {
        Assert.Contains(Orkeon.Constants.Protocol.RunEventKinds.LlmFallback, Orkeon.Constants.Protocol.RunEventKinds.All);
        Assert.Equal(RunEventKinds.LlmFallback, Orkeon.Constants.Protocol.RunEventKinds.LlmFallback);
        Assert.Equal(RunEventKinds.GraphNodeEntered, Orkeon.Constants.Protocol.RunEventKinds.GraphNodeEntered);
        Assert.Contains(Orkeon.Constants.Protocol.RunEventKinds.GraphNodeEntered, Orkeon.Constants.Protocol.RunEventKinds.All);
    }
}
