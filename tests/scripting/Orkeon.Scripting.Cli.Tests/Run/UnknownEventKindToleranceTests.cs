using Orkeon.Scripting.Cli.Commands.Run;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-05, the first lot to add a kind (llm.fallback): the inbound pump neither mistakes a kind it does
/// not handle for the human's answer nor stops reading because of it.
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

    [Fact]
    public void The_fallback_kind_is_a_declared_kind_of_the_protocol()
    {
        Assert.Contains(Orkeon.Constants.Protocol.RunEventKinds.LlmFallback, Orkeon.Constants.Protocol.RunEventKinds.All);
        Assert.Equal(RunEventKinds.LlmFallback, Orkeon.Constants.Protocol.RunEventKinds.LlmFallback);
    }
}
