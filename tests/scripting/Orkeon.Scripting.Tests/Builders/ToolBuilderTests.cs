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

    [Fact]
    public void ToolBuilder_withSchema_reads_every_field_of_a_bare_json_schema()
    {
        var tool = Eval<JsTool>(NewEngine(), """
            toolBuilder()
              .name("var_calculation")
              .description("Computes VaR")
              .withSchema({
                  type: "object",
                  properties: {
                      confidence: { type: "number", description: "Confidence level", default: 0.95,
                                    enum: [0.9, 0.95, 0.99], example: 0.95 },
                      method: { type: "string", description: "Method", format: "identifier" },
                      prices: { type: "array", description: "Series", items: { type: "number" } }
                  },
                  required: ["prices"]
              })
              .execute((input) => 0)
              .build();
            """);

        var confidence = tool.Schema.Parameters["confidence"];
        Assert.Equal("number", confidence.Type);
        Assert.False(confidence.Required);
        Assert.Equal(0.95d, confidence.Default);
        Assert.Equal(0.95d, confidence.Example);
        Assert.NotNull(confidence.Enum);
        Assert.Equal(3, confidence.Enum!.Count);
        Assert.Equal("identifier", tool.Schema.Parameters["method"].Format);
        var prices = tool.Schema.Parameters["prices"];
        Assert.True(prices.Required);
        Assert.Equal("array", prices.Type);
        Assert.Equal("number", prices.ItemsType);
    }

    [Fact]
    public void ToolBuilder_withSchema_accepts_the_typings_input_output_shape()
    {
        // tool.d.ts declares { input, output } — passing that shape used to
        // produce ZERO parameters. Both directions must land now.
        var tool = Eval<JsTool>(NewEngine(), """
            toolBuilder()
              .name("shaped")
              .description("d.ts-shaped schema")
              .withSchema({
                  input: {
                      type: "object",
                      properties: { q: { type: "string", description: "query" } },
                      required: ["q"]
                  },
                  output: { type: "object", properties: { hits: { type: "integer" } } }
              })
              .execute((input) => ({ hits: 1 }))
              .build();
            """);

        Assert.True(tool.HasExplicitSchema);
        var q = tool.Schema.Parameters["q"];
        Assert.Equal("string", q.Type);
        Assert.True(q.Required);
        Assert.NotNull(tool.Schema.Returns);
        Assert.True(tool.Schema.Returns!.ContainsKey("properties"));
    }

    [Fact]
    public void ToolBuilder_access_maps_to_the_domain_enum_and_rejects_junk()
    {
        var tool = Eval<JsTool>(NewEngine(), """
            toolBuilder().name("t").description("d").access("read")
              .execute((i) => i).build();
            """);
        Assert.Equal(Orkeon.Domain.Tools.ToolAccess.Read, tool.Access);

        var undeclared = Eval<JsTool>(NewEngine(), """
            toolBuilder().name("t").description("d").execute((i) => i).build();
            """);
        Assert.Equal(Orkeon.Domain.Tools.ToolAccess.Unspecified, undeclared.Access);

        ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                toolBuilder().name("t").description("d").access("root")
                  .execute((i) => i).build();
                """),
            "access");
    }
}
