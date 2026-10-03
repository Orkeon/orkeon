using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Configuration;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Security;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class RateLimitedLlmProviderTests
{
    [Fact]
    public void Name_PassesThroughInner()
    {
        var sut = new RateLimitedLlmProvider(new FakeProvider("inner-name"), new AlwaysAcquire());
        Assert.Equal("inner-name", sut.Name);
    }

    [Fact]
    public void Capabilities_PassThroughInner()
    {
        // GAP-31: the crew's planner reads the capabilities of the provider it plans on — the echo
        // provider says it replays its prompt — and a decorator that hid them reported Unknown.
        var capabilities = new LlmProviderCapabilities { ReplaysPrompt = true, ResponseFormat = ResponseFormatSupport.JsonObject };
        ILlmProvider sut = new RateLimitedLlmProvider(
            new Orkeon.Infrastructure.Tests.Doubles.MockLlmProvider { Capabilities = capabilities }, new AlwaysAcquire());

        Assert.Same(capabilities, sut.Capabilities);
    }

    [Fact]
    public void Wrap_limits_a_provider_once_and_never_one_that_runs_its_own_tools()
    {
        // GAP-38: the entrance limiter. No limiter, nothing to apply; a provider limited already is
        // left as it is; a provider that runs its own tools is not limited — what it calls of Orkeon's
        // model is, where that model is.
        var limiter = new AlwaysAcquire();
        var plain = new FakeProvider();
        var bridge = new Orkeon.Infrastructure.Tests.Doubles.MockLlmProvider
        {
            Capabilities = new LlmProviderCapabilities { RunsOwnTools = true },
        };

        var limited = RateLimitedLlmProvider.Wrap(plain, limiter);

        Assert.Same(plain, RateLimitedLlmProvider.Wrap(plain, rateLimiter: null));
        Assert.IsType<RateLimitedLlmProvider>(limited);
        Assert.Same(limited, RateLimitedLlmProvider.Wrap(limited, limiter));
        Assert.Same(bridge, RateLimitedLlmProvider.Wrap(bridge, limiter));
    }

    [Fact]
    public async Task A_call_made_inside_a_limited_call_takes_no_lease_of_its_own()
    {
        // The mark on the flow: the inner call of a provider that answers through another limited
        // one is covered by the outer lease — under MaxConcurrentRequests: 1 it would wait on itself.
        var limiter = new AlwaysAcquire();
        var inner = RateLimitedLlmProvider.Wrap(new FakeProvider("inner"), limiter);
        var outer = RateLimitedLlmProvider.Wrap(new Orkeon.Infrastructure.Tests.Doubles.MockRelayLlmProvider("outer", inner), limiter);

        await outer.ChatAsync([LlmMessage.User("hi")], cancellationToken: TestContext.Current.CancellationToken);
        await inner.ChatAsync([LlmMessage.User("hi")], cancellationToken: TestContext.Current.CancellationToken);

        // One lease for the nested pair, one for the inner provider called on its own.
        Assert.Equal(2, limiter.AcquireCount);
    }

    [Fact]
    public void Ctor_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RateLimitedLlmProvider(null!, new AlwaysAcquire()));
        Assert.Throws<ArgumentNullException>(() => new RateLimitedLlmProvider(new FakeProvider(), null!));
    }

    [Fact]
    public async Task GenerateAsync_AcquiresLease_CallsInner_ThenDisposesLease()
    {
        using var lease = new TrackingLease();
        var limiter = new AlwaysAcquire(lease);
        var inner = new FakeProvider();
        var sut = new RateLimitedLlmProvider(inner, limiter);

        var resp = await sut.GenerateAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, limiter.AcquireCount);
        Assert.Equal(1, inner.GenerateCount);
        Assert.True(lease.Disposed);            // lease released after the call
        Assert.Equal("ok", resp.Content);
    }

    [Fact]
    public async Task ChatAsync_AcquiresLease_CallsInner()
    {
        var limiter = new AlwaysAcquire();
        var inner = new FakeProvider();
        var sut = new RateLimitedLlmProvider(inner, limiter);

        await sut.ChatAsync([LlmMessage.User("hi")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, limiter.AcquireCount);
        Assert.Equal(1, inner.ChatCount);
    }

    [Fact]
    public async Task Concurrency_NeverExceedsMaxConcurrentRequests()
    {
        // Only the concurrency gate should bite — keep every per-minute window wide open.
        var options = new RateLimitingOptions
        {
            MaxConcurrentRequests = 3,
            GlobalRequestsPerMinute = 100_000,
            ProviderRequestsPerMinute = 100_000,
            QueueLimit = 1_000,
        };
        using var realLimiter = new LlmRateLimiter(Options.Create(options), NullLogger<LlmRateLimiter>.Instance);
        var inner = new ConcurrencyProbingProvider(holdMs: 40);
        var sut = new RateLimitedLlmProvider(inner, realLimiter);

        var calls = Enumerable.Range(0, 30).Select(_ => sut.GenerateAsync("x"));
        await Task.WhenAll(calls);

        Assert.Equal(30, inner.TotalCalls);
        Assert.True(inner.MaxObservedConcurrency <= 3,
            $"max in-flight {inner.MaxObservedConcurrency} exceeded MaxConcurrentRequests=3");
    }

    [Fact]
    public async Task Denied_RetriesUntilAcquired_ThenCallsInner()
    {
        var limiter = new DenyThenAcquire(denials: 2);
        var inner = new FakeProvider();
        var sut = new RateLimitedLlmProvider(inner, limiter, maxAcquireRetries: 5);

        await sut.GenerateAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, limiter.AcquireCount); // 2 denied + 1 acquired
        Assert.Equal(1, inner.GenerateCount);
    }

    [Fact]
    public async Task Denied_ExhaustsRetries_Throws_AndNeverCallsInner()
    {
        var limiter = new AlwaysDeny();
        var inner = new FakeProvider();
        var sut = new RateLimitedLlmProvider(inner, limiter, maxAcquireRetries: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GenerateAsync("hi", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, inner.GenerateCount);
    }

    // ---- Test doubles ----

    private sealed class FakeProvider(string name = "fake") : ILlmProvider
    {
        public int GenerateCount;
        public int ChatCount;
        public string Name => name;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref GenerateCount);
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ChatCount);
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }
    }

    private sealed class ConcurrencyProbingProvider(int holdMs) : ILlmProvider
    {
        private int _current;
        public int MaxObservedConcurrency;
        public int TotalCalls;
        public string Name => "probe";

        public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref TotalCalls);
            var now = Interlocked.Increment(ref _current);
            int observed;
            do { observed = MaxObservedConcurrency; }
            while (now > observed &&
                   Interlocked.CompareExchange(ref MaxObservedConcurrency, now, observed) != observed);
            try { await Task.Delay(holdMs, ct); }
            finally { Interlocked.Decrement(ref _current); }
            return new LlmResponse { Content = "ok" };
        }

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => GenerateAsync(string.Empty, config, ct);
    }

    private sealed class TrackingLease : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private sealed class AlwaysAcquire(TrackingLease? lease = null) : ILlmRateLimiter
    {
        public int AcquireCount;
        private readonly IDisposable _lease = (IDisposable?)lease ?? new NoopLease();

        public Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AcquireCount);
            return Task.FromResult(RateLimitAcquisition.Acquired(_lease));
        }

        private sealed class NoopLease : IDisposable { public void Dispose() { } }
    }

    private sealed class AlwaysDeny : ILlmRateLimiter
    {
        public Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
            => Task.FromResult(RateLimitAcquisition.Denied("always", TimeSpan.FromMilliseconds(1)));
    }

    private sealed class DenyThenAcquire(int denials) : ILlmRateLimiter
    {
        public int AcquireCount;
        private int _remaining = denials;

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The Lease (no-op Dispose) is owned by the returned RateLimitAcquisition; the SUT disposes it after the call.")]
        public Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AcquireCount);
            return Task.FromResult(Interlocked.Decrement(ref _remaining) >= 0
                ? RateLimitAcquisition.Denied("warmup", TimeSpan.FromMilliseconds(1))
                : RateLimitAcquisition.Acquired(new Lease()));
        }

        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}
