using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Builders;

/// <summary>
/// Scripting surface of the JSON Schema constraint (LLM-03): the DSL must be able to declare
/// a schema end-to-end, not just the scalar format string it was limited to.
/// </summary>
public sealed class JsResponseSchemaTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsCrew Evaluate(string script) => (JsCrew)NewEngine().Evaluate(script).ToObject()!;

    [Fact]
    public void WithResponseSchema_OnAgent_PropagatesSchemaToAgentConfiguration()
    {
        var crew = Evaluate("""
            const a = agentBuilder()
                .name('extractor')
                .role('Invoice extractor')
                .goal('extract')
                .llm({ provider: 'openai', model: 'gpt-5.6-sol' })
                .withResponseSchema('invoice', {
                    type: 'object',
                    properties: { total: { type: 'number' } },
                    required: ['total']
                })
                .build();

            const t = taskBuilder()
                .description('extract json')
                .expectedOutput('json')
                .agent(a)
                .build();

            crewBuilder().name('crew').process('sequential').withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var format = Assert.Single(config.Agents).LlmConfig!.ResponseFormat!;
        Assert.Equal("json_schema", format.Type);
        Assert.Equal("invoice", format.Schema!.Name);
        Assert.True(format.Schema.Strict);
        Assert.Contains("\"total\"", format.Schema.Schema, StringComparison.Ordinal);
    }

    [Fact]
    public void WithResponseSchema_OnTask_ProducesAnLlmOverride()
    {
        var crew = Evaluate("""
            const a = agentBuilder().name('w').role('worker').goal('g').build();

            const t = taskBuilder()
                .description('extract json')
                .expectedOutput('json')
                .agent(a)
                .withResponseSchema('extraction', { type: 'object' }, false)
                .build();

            crewBuilder().name('crew').process('sequential').withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var format = Assert.Single(config.Tasks).LlmOverride!.ResponseFormat!;
        Assert.Equal("json_schema", format.Type);
        Assert.Equal("extraction", format.Schema!.Name);
        Assert.False(format.Schema.Strict);
    }

    [Fact]
    public void WithResponseFormat_WithoutSchema_KeepsWorkingUnchanged()
    {
        var crew = Evaluate("""
            const a = agentBuilder()
                .name('a').role('r').goal('g')
                .withResponseFormat('json_object')
                .build();

            const t = taskBuilder().description('d').expectedOutput('o').agent(a).build();
            crewBuilder().name('crew').process('sequential').withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var format = Assert.Single(config.Agents).LlmConfig!.ResponseFormat!;
        Assert.Equal("json_object", format.Type);
        Assert.Null(format.Schema);
    }
}
