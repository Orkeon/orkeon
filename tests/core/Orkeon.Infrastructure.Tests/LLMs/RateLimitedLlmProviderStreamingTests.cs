using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// exp07 F5: streaming passthrough of <see cref="RateLimitedLlmProvider"/> — the lease is
/// acquired before the first delta and held for the whole enumeration (closes the previous
/// throttling bypass of the streaming path); a non-streaming inner provider falls back to
/// a buffered single-delta stream under the same lease.
/// </summary>
public class RateLimitedLlmProviderStreamingTests
{
    private static readonly LlmMessage[] OneUserMessage = [LlmMessage.User("hi")];
    private static readonly string[] AbDeltas = ["a", "b"];
    private static readonly string[] XChunk = ["x"];

    [Fact]
    public void SupportsStreaming_reflects_the_inner_provider()
    {
        var streamingInner = new MockStreamingLlmProvider();
        Assert.True(new RateLimitedLlmProvider(streamingInner, new AlwaysAcquire()).SupportsStreaming);

        streamingInner.SupportsStreaming = false;
        Assert.False(new RateLimitedLlmProvider(streamingInner, new AlwaysAcquire()).SupportsStreaming);

        Assert.False(new RateLimitedLlmProvider(new NonStreamingProvider(), new AlwaysAcquire()).SupportsStreaming);
    }

    [Fact]
    public async Task ChatStreaming_acquires_before_first_delta_and_releases_after_enumeration()
    {
        var inner = new MockStreamingLlmProvider();
        inner.SetStreamingChunks(["a", "b"]);
        using var lease = new TrackingLease();
        var limiter = new AlwaysAcquire(lease);
        var sut = new RateLimitedLlmProvider(inner, limiter);

        var deltas = new List<string>();
        await foreach (var ev in sut.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, limiter.AcquireCount);   // lease taken before the first event
            Assert.False(lease.Disposed);            // and held while streaming
            if (ev.Kind == LlmStreamEventKind.ContentDelta) deltas.Add(ev.Delta!);
        }

        Assert.Equal(AbDeltas, deltas);
        Assert.True(lease.Disposed);                 // released once the stream ends
        Assert.Equal(1, inner.ChatStreamingCallCount);
    }

    [Fact]
    public async Task ChatStreaming_with_non_streaming_inner_falls_back_to_buffered_single_delta()
    {
        var inner = new NonStreamingProvider();
        var limiter = new AlwaysAcquire();
        var sut = new RateLimitedLlmProvider(inner, limiter);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in sut.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(ev);

        Assert.Equal(1, limiter.AcquireCount);
        Assert.Single(events, e => e.Kind == LlmStreamEventKind.ContentDelta && e.Delta == "buffered");
        Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
    }

    [Fact]
    public async Task GenerateStreaming_passes_through_under_a_lease()
    {
        var inner = new MockStreamingLlmProvider();
        inner.SetStreamingChunks(["x"]);
        using var lease = new TrackingLease();
        var limiter = new AlwaysAcquire(lease);
        var sut = new RateLimitedLlmProvider(inner, limiter);

        var chunks = new List<string>();
        await foreach (var chunk in sut.GenerateStreamingAsync("p", cancellationToken: TestContext.Current.CancellationToken))
            chunks.Add(chunk);

        Assert.Equal(XChunk, chunks);
        Assert.Equal(1, limiter.AcquireCount);
        Assert.True(lease.Disposed);
    }

    /// <summary>
    /// D5-03: an unconfigured inner provider declares <c>SupportsStreaming = false</c>, so the
    /// decorator takes the buffered fallback — and that fallback used to yield nothing at all,
    /// because the missing-key answer carries its sentence in the metadata and an empty
    /// <c>Content</c>. The caller then saw a stream that ended normally on silence, which is the
    /// exact defect the capability declaration was changed to remove one layer down.
    /// </summary>
    [Fact]
    public async Task GenerateStreaming_with_unconfigured_inner_fails_instead_of_ending_silently()
    {
        var inner = new UnconfiguredProvider();
        using var lease = new TrackingLease();
        var sut = new RateLimitedLlmProvider(inner, new AlwaysAcquire(lease));

        var chunks = new List<string>();
        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var chunk in sut.GenerateStreamingAsync("p", cancellationToken: TestContext.Current.CancellationToken))
                chunks.Add(chunk);
        });

        Assert.Contains(UnconfiguredProvider.MissingKeyError, failure.Message, StringComparison.Ordinal);
        Assert.Empty(chunks);
        Assert.True(lease.Disposed);   // the lease is released even when the fallback fails
    }

    /// <summary>
    /// The counterpart of the test above: a provider that genuinely answered nothing carries no
    /// error, and that stays an empty stream — only a refusal becomes an exception.
    /// </summary>
    [Fact]
    public async Task GenerateStreaming_with_an_empty_answer_and_no_error_still_ends_normally()
    {
        var inner = new NonStreamingProvider { Answer = "" };
        var sut = new RateLimitedLlmProvider(inner, new AlwaysAcquire());

        var chunks = new List<string>();
        await foreach (var chunk in sut.GenerateStreamingAsync("p", cancellationToken: TestContext.Current.CancellationToken))
            chunks.Add(chunk);

        Assert.Empty(chunks);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class NonStreamingProvider : ILlmProvider
    {
        public string Name => "plain";
        public string Answer { get; init; } = "buffered";
        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = Answer });
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = Answer });
    }

    /// <summary>Shape of every provider whose API key is missing: no content, the reason in the metadata.</summary>
    private sealed class UnconfiguredProvider : ILlmProvider
    {
        public const string MissingKeyError = "OpenAI API key is required";

        public string Name => "unconfigured";

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(MissingKeyResponse);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(MissingKeyResponse);

        private static LlmResponse MissingKeyResponse => new()
        {
            Content = "",
            Metadata = new Dictionary<string, object>
            {
                ["provider"] = "unconfigured",
                ["error"] = MissingKeyError,
            },
        };
    }

    private sealed class TrackingLease : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class AlwaysAcquire(TrackingLease? lease = null) : ILlmRateLimiter
    {
        public int AcquireCount;
        private readonly IDisposable _lease = (IDisposable?)lease ?? new NoopLease();

        public Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AcquireCount);
            return Task.FromResult(RateLimitAcquisition.Acquired(_lease));
        }

        private sealed class NoopLease : IDisposable { public void Dispose() { } }
    }
}
