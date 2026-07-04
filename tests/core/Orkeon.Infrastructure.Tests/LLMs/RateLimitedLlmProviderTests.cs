using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
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
            AgentRequestsPerMinute = 100_000,
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
        var sut = new RateLimitedLlmProvider(inner, limiter, logger: null, maxAcquireRetries: 5);

        await sut.GenerateAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, limiter.AcquireCount); // 2 denied + 1 acquired
        Assert.Equal(1, inner.GenerateCount);
    }

    [Fact]
    public async Task Denied_ExhaustsRetries_Throws_AndNeverCallsInner()
    {
        var limiter = new AlwaysDeny();
        var inner = new FakeProvider();
        var sut = new RateLimitedLlmProvider(inner, limiter, logger: null, maxAcquireRetries: 2);

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

        public Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AcquireCount);
            return Task.FromResult(RateLimitAcquisition.Acquired(_lease));
        }

        private sealed class NoopLease : IDisposable { public void Dispose() { } }
    }

    private sealed class AlwaysDeny : ILlmRateLimiter
    {
        public Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole, CancellationToken ct = default)
            => Task.FromResult(RateLimitAcquisition.Denied("always", TimeSpan.FromMilliseconds(1)));
    }

    private sealed class DenyThenAcquire(int denials) : ILlmRateLimiter
    {
        public int AcquireCount;
        private int _remaining = denials;

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The Lease (no-op Dispose) is owned by the returned RateLimitAcquisition; the SUT disposes it after the call.")]
        public Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AcquireCount);
            return Task.FromResult(Interlocked.Decrement(ref _remaining) >= 0
                ? RateLimitAcquisition.Denied("warmup", TimeSpan.FromMilliseconds(1))
                : RateLimitAcquisition.Acquired(new Lease()));
        }

        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}
