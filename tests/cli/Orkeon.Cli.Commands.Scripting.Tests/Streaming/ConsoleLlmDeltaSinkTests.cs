using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.Commands.Scripting.Streaming;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

namespace Orkeon.Cli.Commands.Scripting.Tests.Streaming;

/// <summary>
/// The native console delta sink writes whole lines, keeps the turns that stream at once apart
/// (#211) and terminates the line at end of turn; its DI registration is a strict config opt-in.
/// </summary>
public sealed class ConsoleLlmDeltaSinkTests
{
    [Fact]
    public void Deltas_are_written_inline_and_the_turn_terminator_closes_the_line()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);

        sink.OnDelta("Hel");
        sink.OnDelta("lo");
        sink.OnTurnCompleted();

        Assert.Equal("Hello" + Environment.NewLine, console.Output);
    }

    [Fact]
    public void A_turn_writes_each_line_once_it_is_complete_and_the_rest_at_its_end()
    {
        var console = new ScriptedTestConsole();
        var turn = new ConsoleLlmDeltaSink(console).BeginTurn(null);

        turn.OnDelta("one\ntw");
        Assert.Equal("one" + Environment.NewLine, console.Output);

        turn.OnDelta("o\r\nthree");
        Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine, console.Output);

        turn.OnTurnCompleted();
        Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine + "three" + Environment.NewLine, console.Output);
    }

    [Fact]
    public void Two_turns_streaming_at_once_never_write_into_one_another()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var first = sink.BeginTurn("t1");
        var second = sink.BeginTurn("t2");

        first.OnDelta("**Test file");
        second.OnDelta("**Stub:** `src/st");
        first.OnDelta(":** `src/math.test.js`\n");
        second.OnDelta("ats.js`\n");
        first.OnTurnCompleted();
        second.OnTurnCompleted();

        Assert.Equal(
            "[t1] **Test file:** `src/math.test.js`" + Environment.NewLine + "[t2] **Stub:** `src/stats.js`" + Environment.NewLine,
            console.Output);
    }

    [Fact]
    public void A_turn_under_a_command_instance_carries_its_ticket_and_the_label()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var instance = new CommandInstance("t2", "review", CommandInstanceKind.Async, "agent", "intent", Guid.NewGuid(), DateTimeOffset.UtcNow);

        ProgressAmbient.CurrentInstance = instance;
        ILlmDeltaSink under, labelled;
        try
        {
            under = sink.BeginTurn(null);
            labelled = sink.BeginTurn("task-1");
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
        }
        var bare = sink.BeginTurn(null);

        under.OnDelta("a\n");
        labelled.OnDelta("b\n");
        bare.OnDelta("c\n");
        under.OnTurnCompleted();
        labelled.OnTurnCompleted();
        bare.OnTurnCompleted();

        Assert.Equal(
            "[t2] a" + Environment.NewLine + "[t2 task-1] b" + Environment.NewLine + "c" + Environment.NewLine + Environment.NewLine,
            console.Output);
    }

    [Fact]
    public void Registration_is_a_config_opt_in()
    {
        var disabled = BuildProvider(enabled: null);
        Assert.Null(disabled.GetService<ILlmDeltaSink>());

        var off = BuildProvider(enabled: "false");
        Assert.Null(off.GetService<ILlmDeltaSink>());

        var on = BuildProvider(enabled: "true");
        Assert.IsType<ConsoleLlmDeltaSink>(on.GetService<ILlmDeltaSink>());
    }

    private static ServiceProvider BuildProvider(string? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null)
            values["Orkeon:Cli:ConsoleStreaming:Enabled"] = enabled;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConsoleAdapter>(new ScriptedTestConsole());
        services.AddLlmConsoleStreaming(configuration);
        return services.BuildServiceProvider();
    }
}
