using Jint;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// F1.5: <c>ctx.llm.interrupt()</c> — a local interrupt settles <c>act</c> gracefully with
/// <c>{ interrupted: true }</c>, while host-token cancellation keeps its hard semantics: the
/// call rejects, and the crew's root pump turns that rejection into the host's
/// <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class JsLlmFacadeInterruptTests
{
    [Fact]
    public async Task Interrupt_before_act_settles_gracefully_without_any_llm_call()
    {
        using var engine = new Engine();
        var provider = new CountingProvider(_ => new LlmResponse { Content = "never" });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        facade.interrupt();
        Assert.True(facade.isInterrupted);

        var result = await facade.ActAsync(engine, "hello", null);

        Assert.True(result.Get("interrupted").AsBoolean());
        Assert.Equal("(interrupted)", result.Get("output").AsString());
        Assert.Equal(0, provider.ChatCalls);
    }

    [Fact]
    public async Task Interrupt_during_chat_stops_the_loop_at_the_next_iteration()
    {
        using var engine = new Engine();
        JsLlmFacade? facadeRef = null;
        // The provider interrupts its own facade mid-call, then keeps asking for tools:
        // the loop must stop at the next iteration's cancellation check.
        var provider = new CountingProvider(_ =>
        {
            facadeRef!.interrupt();
            return new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                                  "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"missing_tool\",\"arguments\":\"{}\"}}]}}]}",
            };
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        facadeRef = facade;

        var result = await facade.ActAsync(engine, "loop", null);

        Assert.True(result.Get("interrupted").AsBoolean());
        Assert.Equal(1, provider.ChatCalls);
    }

    [Fact]
    public async Task Host_cancellation_still_throws_OperationCanceledException()
    {
        using var engine = new Engine();
        using var hostCts = new CancellationTokenSource();
        var provider = new CountingProvider(_ => new LlmResponse { Content = "never" });
        var facade = new JsLlmFacade(engine, provider, hostCts.Token);

        await hostCts.CancelAsync();

        // The loop's Task is cancelled and act rejects — never the graceful { interrupted }
        // result; seen from the host (ActAsHostAsync applies the root pump's rule) that is
        // the host's own OperationCanceledException, whatever Jint's task bridge rendered the
        // rejection as. Proven by mutation: a loop settling host cancellation as a local
        // interrupt returns a value here and fails the assertion.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => facade.ActAsHostAsync(engine, "hello", null, hostCts.Token));
        Assert.Equal(0, provider.ChatCalls);
    }

    /// <summary>
    /// The same contract end to end, through the crew that owns the rule: a host cancelled while
    /// <c>act</c> is in flight comes out of <c>crew.RunAsync</c> as the host's
    /// <see cref="OperationCanceledException"/>, never as a settled <c>{ interrupted: true }</c>
    /// the script could keep working from.
    /// </summary>
    [Fact]
    public async Task Host_cancellation_during_act_surfaces_as_OperationCanceledException_from_the_crew()
    {
        using var hostCts = new CancellationTokenSource();
        // The provider suspends before it cancels the host and asks for a tool, so the crew is
        // already draining the body when the loop hits its next cancellation check and the
        // rejection is what the root pump maps. The crew also checks its token before it
        // starts draining; a scheduler that parks the test thread across the provider's whole
        // suspension would let that check answer first — the same exception, so the test can
        // only lose its reach, never fail for the wrong reason.
        var provider = new CountingProvider(_ =>
        {
            hostCts.Cancel();
            return new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                                  "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"missing_tool\",\"arguments\":\"{}\"}}]}}]}",
            };
        }, suspendFirst: true);
        using var engine = new Orkeon.Scripting.JsEngineFactory(llmProvider: provider).Create();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (_input, ctx) => {
                    const r = await ctx.llm.act("loop");
                    return "settled:" + JSON.stringify(r);
                })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => crew.RunAsync(null, hostCts.Token));
        Assert.Equal(1, provider.ChatCalls);
    }

    private static JsCrew BuildCrew(Engine engine, string script)
        => (JsCrew)engine.Evaluate(script).ToObject()!;

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class CountingProvider : ILlmProvider
    {
        private readonly Func<LlmMessage[], LlmResponse> _respond;
        private readonly bool _suspendFirst;
        public int ChatCalls { get; private set; }

        /// <param name="respond">The answer to every chat call.</param>
        /// <param name="suspendFirst">
        /// Suspend for a moment before answering, so the call is a real suspension and whatever
        /// the callback does (cancel the host, say) happens while the crew is draining the body
        /// — not before the crew's own token check, which would settle the case without the
        /// loop ever being involved. A yield alone is not enough: the pool continuation wins
        /// the race to that check.
        /// </param>
        public CountingProvider(Func<LlmMessage[], LlmResponse> respond, bool suspendFirst = false)
        {
            _respond = respond;
            _suspendFirst = suspendFirst;
        }

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_respond(Array.Empty<LlmMessage>()));

        public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_suspendFirst)
                await Task.Delay(100, ct);
            ChatCalls++;
            return _respond(messages);
        }
    }
}
