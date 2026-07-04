using Jint;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class ToolBuilderTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js)
        => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public void ToolBuilder_minimum_builds()
    {
        var tool = Eval<JsTool>(NewEngine(), """
            toolBuilder()
              .name("echo")
              .description("returns the input as-is")
              .execute((input, ctx) => ({ echoed: input.value }))
              .build();
            """);

        Assert.Equal("echo", tool.Name);
        Assert.Equal("returns the input as-is", tool.Description);
        Assert.False(tool.HasExplicitSchema);
    }

    [Fact]
    public void ToolBuilder_missing_execute_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""toolBuilder().name("x").description("y").build();"""),
            "execute");

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ToolBuilder_execute_callback_invoked_via_CallAsync()
    {
        var tool = Eval<JsTool>(NewEngine(), """
            toolBuilder()
              .name("plus")
              .description("adds two numbers")
              .execute((input, ctx) => input.a + input.b)
              .build();
            """);

        var response = await tool.CallAsync(
            new ToolCallRequest("plus", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.Equal(5d, Convert.ToDouble(response.Result));
    }

    [Fact]
    public void ToolBuilder_with_schema_attaches_parameters()
    {
        var tool = Eval<JsTool>(NewEngine(), """
            toolBuilder()
              .name("fetch")
              .description("fetches a URL")
              .withSchema({
                type: "object",
                properties: { url: { type: "string", description: "URL to fetch" } },
                required: ["url"]
              })
              .execute((input, ctx) => ({ content: "ok" }))
              .build();
            """);

        Assert.True(tool.HasExplicitSchema);
        Assert.Contains("url", tool.Schema.Parameters.Keys);
        Assert.True(tool.Schema.Parameters["url"].Required);
        Assert.Equal("string", tool.Schema.Parameters["url"].Type);
    }

    [Fact]
    public void Crew_with_autonomous_tool_without_schema_logs_warning()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var engine = new JsEngineFactory(loggerFactory: loggerFactory).Create();

        engine.Evaluate("""
            const t = toolBuilder().name("noSchemaTool").description("d").execute(() => null).build();
            const a = agentBuilder().name("A").role("R").goal("G").withAutonomousTool(t).build();
            crewBuilder().withAgent(a).build();
            """);

        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("noSchemaTool")));
    }
}
