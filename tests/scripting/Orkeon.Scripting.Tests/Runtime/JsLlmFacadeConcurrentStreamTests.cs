using Jint;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using System.Runtime.CompilerServices;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// SEVEN concurrent <c>for await</c> loops over <c>ctx.llm.stream</c> on ONE Jint
/// engine — the shape of exp02's gap-profile area fan-out.
/// </summary>
/// <remarks>
/// <para>This is a regression test for a defect that reached production: the first
/// version of the stream side-channel called JS callbacks (<c>onReasoning</c> /
/// <c>onComplete</c>) from the CLR stream loop. Jint's <c>Engine</c> is
/// single-threaded, so with seven enumerations in flight the first callback
/// re-entered it from the wrong thread and <c>ScriptFunction.Call</c> threw
/// NullReferenceException INSIDE the engine.</para>
/// <para>What that cost, on exp02 round-42: all seven area writers fell back to a
/// placeholder fragment, the script died silently right after assembling the
/// document, and six of the seven HTTP calls had already returned 200. Nothing in
/// the round's log named a cause — the engine was too broken to log. It read like
/// a network failure, because one of the seven connections had also genuinely
/// failed its TLS handshake, which is what a reader saw first.</para>
/// <para>Observations now live in CLR state read through interop, so nothing in
/// the stream path enters the engine. The `without_callbacks` case already passed
/// before the fix — the adapter itself was never the problem, which is why both
/// cases are kept.</para>
/// </remarks>
public sealed class JsLlmFacadeConcurrentStreamTests
{
    [Fact]
    public async Task Seven_concurrent_streams_report_their_own_usage_and_reasoning()
    {
        using var engine = new Engine();
        var provider = new InterleavingProvider();
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var script = await engine.EvaluateAsync("""
            (async function () {
                const areas = ['a','b','c','d','e','f','g'];
                const out = await Promise.all(areas.map(async (id) => {
                    let text = '';
                    const s = llm.stream('p:' + id);
                    for await (const c of s) text += c;
                    // Read AFTER the loop: the script is what is executing here.
                    return id + ':' + text + ':' + s.reasoningChunks
                         + ':' + s.usage.completionTokens;
                }));
                return out.join('|');
            })()
            """, cancellationToken: TestContext.Current.CancellationToken);
        var settled = script.IsPromise()
            ? await script.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken)
            : script;
        // Each stream carries ITS OWN observations — seven independent side-channels,
        // not one shared counter.
        Assert.Equal(
            "a:OK:2:2|b:OK:2:2|c:OK:2:2|d:OK:2:2|e:OK:2:2|f:OK:2:2|g:OK:2:2",
            settled.AsString());
    }

    [Fact]
    public async Task Seven_concurrent_streams_yield_their_own_text()
    {
        using var engine = new Engine();
        var provider = new InterleavingProvider();
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var script = await engine.EvaluateAsync("""
            (async function () {
                const areas = ['a','b','c','d','e','f','g'];
                const out = await Promise.all(areas.map(async (id) => {
                    let text = '';
                    for await (const c of llm.stream('p:' + id)) text += c;
                    return id + ':' + text;
                }));
                return out.join('|');
            })()
            """, cancellationToken: TestContext.Current.CancellationToken);
        var settled = script.IsPromise()
            ? await script.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken)
            : script;
        Assert.Equal("a:OK|b:OK|c:OK|d:OK|e:OK|f:OK|g:OK", settled.AsString());
    }

    /// <summary>Yields between every delta so the enumerations genuinely interleave.</summary>
    private sealed class InterleavingProvider : ILlmProvider, IStreamingLlmProvider
    {
        public string Name => "interleaving";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake" };
        public bool SupportsStreaming => true;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "OK" });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "OK" });

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return "OK";
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return LlmStreamEvent.Reasoning("think1");
            await Task.Yield();
            yield return LlmStreamEvent.Reasoning("think2");
            await Task.Yield();
            yield return LlmStreamEvent.Content("O");
            await Task.Yield();
            yield return LlmStreamEvent.Content("K");
            await Task.Yield();
            yield return LlmStreamEvent.Complete(new LlmResponse { Content = "OK", PromptTokens = 1, CompletionTokens = 2 });
        }
    }
}
