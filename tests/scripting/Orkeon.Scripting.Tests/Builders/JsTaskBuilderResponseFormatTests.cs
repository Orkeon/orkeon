using Jint;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class JsTaskBuilderResponseFormatTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    [Fact]
    public void WithResponseFormat_PropagatesToTaskConfiguration_ViaAdapter()
    {
        var engine = NewEngine();
        var crew = (JsCrew)engine.Evaluate("""
            const a = agentBuilder()
                .name('worker')
                .role('worker')
                .goal('do')
                .build();

            const t = taskBuilder()
                .description('extract invoice fields as json')
                .expectedOutput('json')
                .agent(a)
                .withResponseFormat('json_object')
                .build();

            crewBuilder()
                .name('crew')
                .process('sequential')
                .withAgent(a)
                .withTask(t)
                .build();
            """).ToObject()!;

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var taskCfg = Assert.Single(config.Tasks);
        Assert.NotNull(taskCfg.LlmOverride);
        Assert.NotNull(taskCfg.LlmOverride!.ResponseFormat);
        Assert.Equal("json_object", taskCfg.LlmOverride.ResponseFormat!.Type);
    }

    [Fact]
    public void WithResponseFormat_Text_KeepsLlmOverrideNull()
    {
        var engine = NewEngine();
        var crew = (JsCrew)engine.Evaluate("""
            const a = agentBuilder().name('w').role('w').goal('g').build();
            const t = taskBuilder().description('d').expectedOutput('o').agent(a).withResponseFormat('text').build();
            crewBuilder().name('c').process('sequential').withAgent(a).withTask(t).build();
            """).ToObject()!;

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.Single(config.Tasks);
        Assert.Null(config.Tasks[0].LlmOverride);
    }
}
