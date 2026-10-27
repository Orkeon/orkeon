using Orkeon.Tests.Shared.Timing;
using Jint;
using Orkeon.Cli.Commands.Scripting.Bindings;
using Orkeon.Cli.Commands.Scripting.Loading;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

public sealed class DefineAsyncCommandCollectorTests
{
    private static (Engine, CommandDescriptorCollector) NewEngine()
    {
        var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var collector = new CommandDescriptorCollector("/test.cmd.ts");
        DefineCommandBinding.Register(engine, collector);
        DefineAsyncCommandBinding.Register(engine, collector);
        return (engine, collector);
    }

    [Fact]
    public void defineAsyncCommand_captures_kind_dispatch_completed_and_maxConcurrent()
    {
        var (engine, collector) = NewEngine();
        engine.Evaluate("""
            defineAsyncCommand({
              name: "askbg", description: "d", maxConcurrent: 3,
              dispatch: function(args, ctx) {}, completed: function(r, ctx) {}
            });
            """);

        var d = Assert.Single(collector.Descriptors);
        Assert.Equal(CommandKind.Async, d.Kind);
        Assert.Equal(3, d.MaxConcurrent);
        Assert.NotNull(d.Dispatch);
        Assert.NotNull(d.Completed);
        Assert.Null(d.Handler);
    }

    [Fact]
    public void defineAsyncCommand_allows_omitting_completed_and_maxConcurrent()
    {
        var (engine, collector) = NewEngine();
        engine.Evaluate("""defineAsyncCommand({ name: "a", description: "d", dispatch: function() {} });""");

        var d = Assert.Single(collector.Descriptors);
        Assert.Equal(CommandKind.Async, d.Kind);
        Assert.Null(d.MaxConcurrent); // omitted ⇒ unbounded
        Assert.Null(d.Completed);
    }

    [Fact]
    public void defineCommand_and_defineAsyncCommand_capture_the_declared_group()
    {
        var (engine, collector) = NewEngine();
        engine.Evaluate("""
            defineCommand({ name: "s", description: "d", group: "session", handler: function() {} });
            defineAsyncCommand({ name: "a", description: "d", group: "dev", dispatch: function() {} });
            defineCommand({ name: "n", description: "d", handler: function() {} });
            """);

        Assert.Equal(["session", "dev", null], collector.Descriptors.Select(d => d.Group));
    }

    [Fact]
    public void defineCommand_rejects_a_non_string_group()
        => AssertThrows("""defineCommand({ name: "a", description: "d", group: 3, handler: function() {} });""");

    [Fact]
    public void defineAsyncCommand_rejects_a_non_string_group()
        => AssertThrows("""defineAsyncCommand({ name: "a", description: "d", group: {}, dispatch: function() {} });""");

    [Fact]
    public void defineAsyncCommand_requires_dispatch()
        => AssertThrows("""defineAsyncCommand({ name: "a", description: "d" });""");

    [Fact]
    public void defineAsyncCommand_rejects_non_integer_maxConcurrent()
        => AssertThrows("""defineAsyncCommand({ name: "a", description: "d", maxConcurrent: 1.5, dispatch: function() {} });""");

    [Fact]
    public void defineAsyncCommand_rejects_zero_maxConcurrent()
        => AssertThrows("""defineAsyncCommand({ name: "a", description: "d", maxConcurrent: 0, dispatch: function() {} });""");

    [Fact]
    public void defineCommand_marks_sync_kind()
    {
        var (engine, collector) = NewEngine();
        engine.Evaluate("""defineCommand({ name: "x", description: "d", handler: function() {} });""");

        var d = Assert.Single(collector.Descriptors);
        Assert.Equal(CommandKind.Sync, d.Kind);
        Assert.NotNull(d.Handler);
    }

    private static void AssertThrows(string js)
    {
        var (engine, _) = NewEngine();
        Assert.ThrowsAny<Exception>(() => engine.Evaluate(js));
    }
}
