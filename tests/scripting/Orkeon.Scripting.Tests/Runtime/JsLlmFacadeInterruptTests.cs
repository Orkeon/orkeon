using Jint;
using Jint.Runtime;
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
    public async Task Host_cancellation_still_rejects_act_instead_of_settling_gracefully()
    {
        using var engine = new Engine();
        using var hostCts = new CancellationTokenSource();
        var provider = new CountingProvider(_ => new LlmResponse { Content = "never" });
        var facade = new JsLlmFacade(engine, provider, hostCts.Token);

        await hostCts.CancelAsync();

        // The loop's Task is cancelled, which Jint's task bridge reports as its own
        // ExecutionCanceledException on the rejection — never the graceful { interrupted }
        // result. JsCrew.UnwrapPromise maps a rejection under a cancelled token to the host's
        // OperationCanceledException; that rule is the crew's, not the facade's.
        await Assert.ThrowsAsync<ExecutionCanceledException>(() => facade.ActAsync(engine, "hello", null));
        Assert.Equal(0, provider.ChatCalls);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class CountingProvider : ILlmProvider
    {
        private readonly Func<LlmMessage[], LlmResponse> _respond;
        public int ChatCalls { get; private set; }

        public CountingProvider(Func<LlmMessage[], LlmResponse> respond) => _respond = respond;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_respond(Array.Empty<LlmMessage>()));

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ChatCalls++;
            return Task.FromResult(_respond(messages));
        }
    }
}
