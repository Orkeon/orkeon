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
/// The native console delta sink streams the foreground act as it arrives, writes every other turn
/// whole line by whole line and keeps the turns that stream at once apart (#211); its DI registration
/// is a strict config opt-in.
/// </summary>
public sealed class ConsoleLlmDeltaSinkTests
{
    private static readonly string NL = Environment.NewLine;

    [Fact]
    public void Deltas_are_written_inline_and_the_turn_terminator_closes_the_line()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);

        sink.OnDelta("Hel");
        sink.OnDelta("lo");
        sink.OnTurnCompleted();

        Assert.Equal("Hello" + NL, console.Output);
    }

    [Fact]
    public void The_foreground_turn_streams_as_it_arrives()
    {
        var console = new ScriptedTestConsole();
        var turn = new ConsoleLlmDeltaSink(console).BeginTurn(null);

        turn.OnDelta("one\ntw");
        Assert.Equal("one\ntw", console.Output);

        turn.OnDelta("o");
        turn.OnTurnCompleted();
        Assert.Equal("one\ntwo" + NL, console.Output);
    }

    [Fact]
    public void A_labelled_turn_writes_each_line_once_it_is_complete_and_the_rest_at_its_end()
    {
        var console = new ScriptedTestConsole();
        var turn = new ConsoleLlmDeltaSink(console).BeginTurn("t1");

        turn.OnDelta("one\ntw");
        Assert.Equal("[t1] one" + NL, console.Output);

        turn.OnDelta("o\r\nthree");
        Assert.Equal("[t1] one" + NL + "[t1] two" + NL, console.Output);

        turn.OnTurnCompleted();
        Assert.Equal("[t1] one" + NL + "[t1] two" + NL + "[t1] three" + NL, console.Output);
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

        Assert.Equal("[t1] **Test file:** `src/math.test.js`" + NL + "[t2] **Stub:** `src/stats.js`" + NL, console.Output);
    }

    [Fact]
    public void A_background_line_waits_for_the_foreground_line_to_end()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var foreground = sink.BeginTurn(null);
        var background = sink.BeginTurn("t2");

        foreground.OnDelta("Hello wor");
        background.OnDelta("done\n");
        Assert.Equal("Hello wor", console.Output);

        foreground.OnDelta("ld\n");
        Assert.Equal("Hello world\n" + "[t2] done" + NL, console.Output);

        background.OnTurnCompleted();
        foreground.OnTurnCompleted();
        Assert.Equal("Hello world\n" + "[t2] done" + NL + NL, console.Output);
    }

    [Fact]
    public void A_second_unlabelled_turn_writes_whole_lines_while_the_first_streams()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);
        var first = sink.BeginTurn(null);
        var second = sink.BeginTurn(null);

        first.OnDelta("a");
        second.OnDelta("x\ny");
        first.OnDelta("b\n");
        second.OnTurnCompleted();
        first.OnTurnCompleted();

        Assert.Equal("ab\n" + "x" + NL + "y" + NL + NL, console.Output);
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

        Assert.Equal("[t2] a" + NL + "[t2 task-1] b" + NL + "c\n" + NL, console.Output);
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
