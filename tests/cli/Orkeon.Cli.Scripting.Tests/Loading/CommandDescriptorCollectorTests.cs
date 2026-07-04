using Jint;
using Orkeon.Cli.Scripting.Bindings;
using Orkeon.Cli.Scripting.Loading;

namespace Orkeon.Cli.Scripting.Tests.Loading;

public sealed class CommandDescriptorCollectorTests
{
    private const string VirtualPath = "/cmd/sample.cmd.ts";

    private static readonly string[] DeployAliases = ["d", "ship"];

    private static (Engine Engine, CommandDescriptorCollector Collector) NewBoundEngine()
    {
        var engine = new Engine();
        var collector = new CommandDescriptorCollector(VirtualPath);
        DefineCommandBinding.Register(engine, collector);
        return (engine, collector);
    }

    [Fact]
    public void Add_captures_single_descriptor()
    {
        var (engine, collector) = NewBoundEngine();
        engine.Evaluate("""
            defineCommand({
              name: "hello",
              description: "Say hi.",
              handler: function (a, c) { return c.continue(); }
            });
        """);

        Assert.Single(collector.Descriptors);
        var d = collector.Descriptors[0];
        Assert.Equal("hello", d.Name);
        Assert.Equal("Say hi.", d.Description);
        Assert.Equal(VirtualPath, d.SourceVirtualPath);
        Assert.Empty(d.Aliases);
        Assert.Null(d.ArgsSchemaJson);
    }

    [Fact]
    public void Add_captures_aliases()
    {
        var (engine, collector) = NewBoundEngine();
        engine.Evaluate("""
            defineCommand({
              name: "deploy",
              aliases: ["d", "ship"],
              description: "Deploy.",
              handler: function (a, c) { return c.continue(); }
            });
        """);
        Assert.Single(collector.Descriptors);
        Assert.Equal(DeployAliases, collector.Descriptors[0].Aliases);
    }

    [Fact]
    public void Add_captures_multiple_descriptors_in_order()
    {
        var (engine, collector) = NewBoundEngine();
        engine.Evaluate("""
            defineCommand({ name: "a", description: "A.", handler: function(){ } });
            defineCommand({ name: "b", description: "B.", handler: function(){ } });
        """);
        Assert.Equal(2, collector.Descriptors.Count);
        Assert.Equal("a", collector.Descriptors[0].Name);
        Assert.Equal("b", collector.Descriptors[1].Name);
    }

    [Fact]
    public void Add_captures_args_schema_when_present()
    {
        var (engine, collector) = NewBoundEngine();
        engine.Evaluate("""
            defineCommand({
              name: "deploy",
              description: "Deploy.",
              args: { target: { type: "string" } },
              handler: function (a, c) { return c.continue(); }
            });
        """);
        Assert.NotNull(collector.Descriptors[0].ArgsSchemaJson);
    }

    [Fact]
    public void Freeze_disables_further_adds()
    {
        var (engine, collector) = NewBoundEngine();
        engine.Evaluate("""
            defineCommand({ name: "a", description: "A.", handler: function(){ } });
        """);
        collector.Freeze();

        var ex = Assert.ThrowsAny<Exception>(() =>
            engine.Evaluate("""
                defineCommand({ name: "b", description: "B.", handler: function(){ } });
            """));

        // Jint wraps the CLR InvalidOperationException as a JavaScriptException.
        Assert.Contains("after the script load phase", ex.ToString());
    }

    [Fact]
    public void Add_rejects_missing_handler()
    {
        var (engine, collector) = NewBoundEngine();
        var ex = Assert.ThrowsAny<Exception>(() =>
            engine.Evaluate("""defineCommand({ name: "a", description: "A." });"""));
        Assert.Contains("handler", ex.ToString());
    }

    [Fact]
    public void Add_rejects_non_string_name()
    {
        var (engine, collector) = NewBoundEngine();
        var ex = Assert.ThrowsAny<Exception>(() =>
            engine.Evaluate("""defineCommand({ name: 42, description: "x.", handler: function(){} });"""));
        Assert.Contains("name", ex.ToString());
    }
}
