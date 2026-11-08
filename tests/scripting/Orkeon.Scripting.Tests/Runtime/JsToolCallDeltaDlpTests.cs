using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// FWK-20: the DLP of a host reaches the script's <c>onToolCallDelta</c> whatever the host's delta sink: the host
/// hands it to the engine (<see cref="ScriptingHostPorts.ToolCallDlp"/>), and the arguments of a call arrive held, scanned
/// whole, in one redacted fragment.
/// </summary>
public sealed class JsToolCallDeltaDlpTests
{
    private static readonly ToolCallDelta[] SecretSplit =
    [
        new(0, "call_1", "send_mail", ""),
        new(0, null, null, "{\"key\":\"sk-SEC"),
        new(0, null, null, "RET-123\"}"),
    ];

    private const string Js = """
        const a = agentBuilder().name("a").role("R").goal("G")
            .body(async (input, ctx) => {
                const seen = [];
                await ctx.llm.act("go", { onToolCallDelta: d => seen.push(d.argumentsDelta) });
                return JSON.stringify(seen);
            })
            .build();
        crewBuilder().name("c").withAgent(a).build();
        """;

    private static ToolCallDeltaDlp Dlp(FakeDlpInterceptor interceptor) =>
        new(interceptor, new DlpPolicy { Channel = DlpChannel.ExternalOutput, DefaultAction = DlpAction.Mask });

    private static async Task<string> RunAsync(ScriptingHostPorts ports)
    {
        var provider = new FakeToolCallStreamingLlmProvider(SecretSplit);
        var engine = new JsEngineFactory(llmProvider: provider, hostPorts: ports).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(Js, null, TestContext.Current.CancellationToken)).ToObject()!;
        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);
        return result.tasks[0].output!.ToString()!;
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldHandTheScriptOneRedactedFragment_WhenTheHostHasDlpAndNoDeltaSink()
    {
        var interceptor = new FakeDlpInterceptor();

        var seen = await RunAsync(new ScriptingHostPorts { ToolCallDlp = Dlp(interceptor) });

        Assert.Equal("[\"{\\\"key\\\":\\\"[MASKED]\\\"}\"]", seen);
        Assert.Equal(["{\"key\":\"sk-SECRET-123\"}"], interceptor.Scanned);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldHandTheScriptTheFragmentsAsTheyArrive_WhenTheHostHasNoDlp()
    {
        var seen = await RunAsync(new ScriptingHostPorts());

        Assert.Equal("[\"\",\"{\\\"key\\\":\\\"sk-SEC\",\"RET-123\\\"}\"]", seen);
    }
}
