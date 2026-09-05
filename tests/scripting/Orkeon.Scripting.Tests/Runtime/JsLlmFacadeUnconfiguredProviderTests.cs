using Jint;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using System.Runtime.CompilerServices;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// LLM-00 §8: a provider with no credentials used to declare <c>SupportsStreaming = true</c>,
/// so <c>ctx.llm.stream</c> took the SSE branch and a script's <c>for await</c> completed
/// without a single chunk — silence indistinguishable from a model with nothing to say — while
/// <c>ctx.llm.complete</c> on the SAME provider answered with the provider's refusal. These
/// tests pin the two surfaces to one answer.
/// </summary>
public sealed class JsLlmFacadeUnconfiguredProviderTests
{
    private const string MissingKeyError = "OpenAI API key is required";
    private const string ExpectedMarker = "<undefined-llm:" + MissingKeyError + ">";
    private static readonly string[] ExpectedSingleChunk = [ExpectedMarker];
    private static readonly string[] HelloChunks = ["He", "llo"];

    [Fact]
    public async Task Complete_names_the_missing_credential_instead_of_returning_empty_content()
    {
        using var engine = new Engine();
        var facade = new JsLlmFacade(engine, new UnconfiguredProvider(), CancellationToken.None);

        var answer = await facade.complete("hi", null);

        Assert.Equal(ExpectedMarker, answer);
    }

    [Fact]
    public async Task Stream_gives_the_same_explicit_answer_as_complete_never_an_empty_sequence()
    {
        using var engine = new Engine();
        var provider = new UnconfiguredProvider();
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var chunks = new List<string>();
        await foreach (var chunk in facade.StreamChunks("hi", null))
            chunks.Add(chunk);

        var completed = await facade.complete("hi", null);

        // The heart of the fix: one chunk, carrying exactly what complete says.
        Assert.Equal(ExpectedSingleChunk, chunks);
        Assert.Equal(ExpectedMarker, completed);

        // And it got there by the buffered path, because the provider stopped claiming
        // a capability it cannot honour.
        Assert.False(provider.SupportsStreaming);
        Assert.Equal(0, provider.StreamingCalls);
    }

    /// <summary>
    /// The C# sequence above proves nothing about the JS protocol (a bare CLR
    /// <c>IAsyncEnumerable</c> once passed every C# test while <c>for await</c> threw), so the
    /// script surface gets its own proof: the loop must run its body, not fall through.
    /// </summary>
    [Fact]
    public void Script_for_await_over_an_unconfigured_provider_receives_the_marker()
    {
        using var engine = new Engine();
        var facade = new JsLlmFacade(engine, new UnconfiguredProvider(), CancellationToken.None);
        engine.SetValue("llm", facade);

        var probe = engine.Evaluate(
            "(async function () { const acc = []; "
            + "for await (const c of llm.stream('hi')) acc.push(c); "
            + "return acc.length + '|' + acc.join(''); })");
        var invoked = engine.Invoke(probe);
        var result = invoked.IsPromise()
            ? invoked.UnwrapIfPromise(TestContext.Current.CancellationToken)
            : invoked;

        Assert.Equal("1|" + ExpectedMarker, result.AsString());
    }

    /// <summary>
    /// A configured provider is untouched: it still declares the capability and still streams
    /// chunk by chunk. The fix withholds the declaration, it does not disable streaming.
    /// </summary>
    [Fact]
    public async Task A_configured_streaming_provider_still_streams_chunk_by_chunk()
    {
        using var engine = new Engine();
        var provider = new ConfiguredStreamingProvider();
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var chunks = new List<string>();
        await foreach (var chunk in facade.StreamChunks("hi", null))
            chunks.Add(chunk);

        Assert.Equal(HelloChunks, chunks);
        Assert.Equal(1, provider.StreamingCalls);
    }

    /// <summary>
    /// Stands in for any <c>HttpLlmProviderBase</c> whose config carries no API key: the
    /// buffered path answers with the provider's error metadata and empty content, and the
    /// streaming capability is withheld. Its streaming methods throw so a regression that
    /// re-takes the SSE branch fails loudly rather than silently yielding nothing.
    /// </summary>
    private sealed class UnconfiguredProvider : ILlmProvider, IStreamingLlmProvider
    {
        public int StreamingCalls { get; private set; }

        public string Name => "openai";

        public LlmConfig? BaseConfig => LlmConfig.Default();

        public bool SupportsStreaming => false;

        private static LlmResponse MissingKey() => new()
        {
            Content = "",
            TokensUsed = 0,
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "openai",
                ["error"] = MissingKeyError,
            },
        };

        public Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(MissingKey());

        public Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(MissingKey());

        public IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            throw new InvalidOperationException("The SSE branch must not be taken on an unconfigured provider.");
        }

        public IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            throw new InvalidOperationException("The SSE branch must not be taken on an unconfigured provider.");
        }
    }

    /// <summary>The control case: credentials present, capability declared, chunks delivered.</summary>
    private sealed class ConfiguredStreamingProvider : ILlmProvider, IStreamingLlmProvider
    {
        private static readonly string[] Chunks = ["He", "llo"];

        public int StreamingCalls { get; private set; }

        public string Name => "openai";

        public LlmConfig? BaseConfig => LlmConfig.Default();

        public bool SupportsStreaming => true;

        public Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Concat(Chunks) });

        public Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = string.Concat(Chunks) });

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            foreach (var chunk in Chunks)
                yield return chunk;
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            foreach (var chunk in Chunks)
                yield return LlmStreamEvent.Content(chunk);
            yield return LlmStreamEvent.Complete(new LlmResponse { Content = string.Concat(Chunks) });
            await Task.CompletedTask;
        }
    }
}
