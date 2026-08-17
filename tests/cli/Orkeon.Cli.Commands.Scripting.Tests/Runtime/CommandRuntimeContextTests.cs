using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

public sealed class CommandRuntimeContextTests
{
    private static (Engine engine, ScriptedTestConsole console, CommandRuntimeContext ctx) Build(CancellationToken ct = default)
    {
        var engine = new Engine();
        var console = new ScriptedTestConsole();
        var ctx = new CommandRuntimeContext(
            engine, console,
            new CommandMeta("deploy", "deploy prod"),
            ct,
            NullLogger.Instance,
            ScriptServiceLocator.Empty);
        return (engine, console, ctx);
    }

    [Fact]
    public void Write_and_writeLine_forward_to_console()
    {
        var (_, console, ctx) = Build(TestContext.Current.CancellationToken);
        ctx.write("Hello, ");
        ctx.writeLine("World");
        Assert.Equal("Hello, World" + Environment.NewLine, console.Output);
    }

    [Fact]
    public void Clear_invokes_console_clear()
    {
        var (_, console, ctx) = Build(TestContext.Current.CancellationToken);
        ctx.writeLine("noise");
        ctx.clear();
        Assert.Equal(string.Empty, console.Output);
    }

    [Fact]
    public void Continue_and_exit_helpers_produce_expected_action_results()
    {
        var (_, _, ctx) = Build(TestContext.Current.CancellationToken);
        Assert.False(ctx.@continue().exit);
        Assert.Null(ctx.@continue().message);
        Assert.Equal("done", ctx.@continue("done").message);
        Assert.True(ctx.exit().exit);
        Assert.Equal("bye", ctx.exit("bye").message);
    }

    [Fact]
    public void Command_metadata_is_exposed_to_handler()
    {
        var (engine, _, ctx) = Build(TestContext.Current.CancellationToken);
        engine.SetValue("ctx", ctx);
        var nameVal = engine.Evaluate("ctx.command.name");
        var rawVal = engine.Evaluate("ctx.command.rawInput");
        Assert.Equal("deploy", nameVal.AsString());
        Assert.Equal("deploy prod", rawVal.AsString());
    }

    [Fact]
    public void Signal_is_exposed_as_cancellation_token_with_pascal_case_members()
    {
        using var cts = new CancellationTokenSource();
        var (engine, _, ctx) = Build(cts.Token);
        engine.SetValue("ctx", ctx);

        var before = engine.Evaluate("ctx.signal.IsCancellationRequested");
        Assert.False(before.AsBoolean());

        cts.Cancel();
        var after = engine.Evaluate("ctx.signal.IsCancellationRequested");
        Assert.True(after.AsBoolean());
    }

    [Fact]
    public void Progress_handle_is_returned_and_writes_lines()
    {
        var (engine, console, ctx) = Build(TestContext.Current.CancellationToken);
        engine.SetValue("ctx", ctx);
        engine.Evaluate("""
            const p = ctx.progress({ label: "ix", total: 2 });
            p.advance("step1");
            p.done("ok");
        """);
        Assert.Contains("[ix] 1/2 — step1", console.Output);
        Assert.Contains("[ix] done: ok", console.Output);
    }

    [Fact]
    public void Table_renders_columns_and_rows()
    {
        var (engine, console, ctx) = Build(TestContext.Current.CancellationToken);
        engine.SetValue("ctx", ctx);
        engine.Evaluate("""
            ctx.table([
              { name: "alpha", count: 1 },
              { name: "beta",  count: 22 }
            ], ["name", "count"]);
        """);
        var output = console.Output;
        Assert.Contains("name", output);
        Assert.Contains("count", output);
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("22", output);
    }

    [Fact]
    public void Services_locator_phase2_throws_on_get()
    {
        var (_, _, ctx) = Build(TestContext.Current.CancellationToken);
        Assert.False(ctx.services.has("anything"));
        Assert.Throws<InvalidOperationException>(() => ctx.services.get("anything"));
    }
}
