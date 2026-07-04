using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class JsAgentBuilderResponseFormatTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    [Fact]
    public void WithResponseFormat_String_CapturesOnBuilder()
    {
        var builder = new JsAgentBuilder()
            .name("a")
            .role("r")
            .goal("g")
            .withResponseFormat("json_object");

        Assert.Equal("json_object", builder.ResponseFormatValue);
    }

    [Fact]
    public void WithResponseFormat_PropagatesToAgentConfiguration_ViaAdapter()
    {
        var engine = NewEngine();
        var crew = (JsCrew)engine.Evaluate("""
            const a = agentBuilder()
                .name('extractor')
                .role('Invoice extractor')
                .goal('extract')
                .llm({ provider: 'openai', model: 'gpt-4o-mini' })
                .withResponseFormat('json_object')
                .build();

            const t = taskBuilder()
                .description('extract json')
                .expectedOutput('json')
                .agent(a)
                .build();

            crewBuilder()
                .name('crew')
                .process('sequential')
                .withAgent(a)
                .withTask(t)
                .build();
            """).ToObject()!;

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var agentCfg = Assert.Single(config.Agents);
        Assert.NotNull(agentCfg.LlmConfig?.ResponseFormat);
        Assert.Equal("json_object", agentCfg.LlmConfig!.ResponseFormat!.Type);
    }
}
