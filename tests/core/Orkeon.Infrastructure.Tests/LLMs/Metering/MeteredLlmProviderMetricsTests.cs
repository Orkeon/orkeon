using System.Diagnostics.Metrics;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Every model call through the metered provider, with or without a sink, is measured once on the
/// static meter "Orkeon": succeeded, failed or streamed.
/// </summary>
[Collection("OrkeonMeter")]
public sealed class MeteredLlmProviderMetricsTests
{
    private sealed record Measurement(string Instrument, double Value, Dictionary<string, object?> Tags);

    private sealed class Capture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();
        private readonly List<Measurement> _all = [];

        public Capture()
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OrkeonMeter.Name)
                    l.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(i, v, t));
            _listener.Start();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = new Dictionary<string, object?>();
            foreach (var tag in tags)
                map[tag.Key] = tag.Value;
            lock (_gate)
                _all.Add(new Measurement(instrument.Name, value, map));
        }

        public List<Measurement> For(string model)
        {
            lock (_gate)
                return _all.Where(m => m.Tags.TryGetValue("gen_ai.request.model", out var v) && (string?)v == model).ToList();
        }

        /// <summary>The latest reading of an observable instrument, or null when it is not published.</summary>
        public double? Gauge(string name)
        {
            int before;
            lock (_gate)
                before = _all.Count;
            _listener.RecordObservableInstruments();
            lock (_gate)
                return _all.Skip(before).LastOrDefault(m => m.Instrument == name)?.Value;
        }

        public void Dispose() => _listener.Dispose();
    }

    private static LlmResponse Answer(string model, int prompt = 7, int completion = 3) => new()
    {
        Content = "ok",
        TokensUsed = prompt + completion,
        PromptTokens = prompt,
        CompletionTokens = completion,
        Model = model,
    };

    private static LlmMessage[] Conversation() => [LlmMessage.User("hello")];

    private static LlmResponse Billed(LlmResponse response, double charge, string currency) => response with
    {
        Metadata = new Dictionary<string, object>
        {
            [LlmUsageMetadataKeys.Cost] = charge,
            [LlmUsageMetadataKeys.CostCurrency] = currency,
        },
    };

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordADurationAndTwoTokenUsages_WhenACallSucceedsWithoutASink()
    {
        const string model = "f5-metrics-success-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(model));

        await MeteredLlmProvider.Wrap(provider, sink: null).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var seen = capture.For(model);
        var duration = Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.True(duration.Value >= 0);
        var tokens = seen.Where(m => m.Instrument == "gen_ai.client.token.usage").ToList();
        Assert.Equal(2, tokens.Count);
        Assert.Equal(7, Assert.Single(tokens, m => (string?)m.Tags["gen_ai.token.type"] == "input").Value);
        Assert.Equal(3, Assert.Single(tokens, m => (string?)m.Tags["gen_ai.token.type"] == "output").Value);
        Assert.Equal(1, Assert.Single(seen, m => m.Instrument == "orkeon.llm.calls").Value);
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordADurationWithErrorType_WhenACallThrows()
    {
        const string model = "f5-metrics-error-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "throwing-vendor" };
        provider.SetChatException(new InvalidOperationException("boom"));
        var metered = MeteredLlmProvider.Wrap(provider, sink: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => metered.ChatAsync(Conversation(), LlmConfig.Create(model), TestContext.Current.CancellationToken));

        var seen = capture.For(model);
        var duration = Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.Equal("System.InvalidOperationException", duration.Tags["error.type"]);
        Assert.DoesNotContain(seen, m => m.Instrument == "gen_ai.client.token.usage");
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordNoToken_WhenAStreamReportsNoUsage()
    {
        const string model = "f5-metrics-stream-model";
        using var capture = new Capture();
        var provider = new MockStreamingLlmProvider { Name = "vendor" };
        provider.SetStreamingChunks(["one", "two"]);
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink: null);

        await foreach (var _ in metered.GenerateStreamingAsync("p", LlmConfig.Create(model), TestContext.Current.CancellationToken))
        {
        }

        var seen = capture.For(model);
        Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.DoesNotContain(seen, m => m.Instrument == "gen_ai.client.token.usage");
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordOnce_WhenTwoMetersAreNested()
    {
        const string model = "f5-metrics-nested-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(model));
        var sink = new MockLlmUsageSink();
        var inner = MeteredLlmProvider.Wrap(provider, sink: null);
        var outer = MeteredLlmProvider.Wrap(new MockRelayLlmProvider("vendor", inner), sink);

        await outer.ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var seen = capture.For(model);
        Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.Equal(2, seen.Count(m => m.Instrument == "gen_ai.client.token.usage"));
        Assert.Single(sink.Recorded);
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldReportToTheSink_WhenASinklessMeterIsWrappedWithASink()
    {
        const string model = "f5-metrics-sinkless-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(model));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(MeteredLlmProvider.Wrap(provider, sink: null), sink);

        await metered.ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(sink.Recorded);
        Assert.Single(capture.For(model), m => m.Instrument == "gen_ai.client.operation.duration");
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordADurationWithErrorType_WhenABufferedStreamFallbackThrows()
    {
        const string model = "f5-metrics-fallback-throws-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        var failure = new InvalidOperationException("boom");
        provider.SetChatException(failure);
        provider.SetGenerateAsyncFunc((_, _, _) => Task.FromException<LlmResponse>(failure));
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink: null);

        var thrownChat = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in metered.ChatStreamingAsync(Conversation(), LlmConfig.Create(model), TestContext.Current.CancellationToken))
            {
            }
        });
        var thrownText = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in metered.GenerateStreamingAsync("p", LlmConfig.Create(model), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Same(failure, thrownChat);
        Assert.Same(failure, thrownText);
        var seen = capture.For(model);
        var durations = seen.Where(m => m.Instrument == "gen_ai.client.operation.duration").ToList();
        Assert.Equal(2, durations.Count);
        Assert.All(durations, d => Assert.Equal("System.InvalidOperationException", d.Tags["error.type"]));
        Assert.Equal(2, seen.Count(m => m.Instrument == "orkeon.llm.calls"));
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordADurationWithErrorType_WhenTheStreamThrowsBeforeItsFirstChunk()
    {
        const string model = "f5-metrics-throws-before-chunk-model";
        using var capture = new Capture();
        var provider = new MockStreamingLlmProvider { Name = "vendor" };
        var failure = new InvalidOperationException("refused at call");
        provider.FailStreamingCallWith(failure);
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink: null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in metered.ChatStreamingAsync(Conversation(), LlmConfig.Create(model), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Same(failure, thrown);
        var seen = capture.For(model);
        var duration = Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.Equal("System.InvalidOperationException", duration.Tags["error.type"]);
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRaiseTheCostGauge_OnlyWhenTheCostIsInUsd()
    {
        const string model = "f5-metrics-cost-model";
        const string gauge = "orkeon.cost.total_usd";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        var metered = MeteredLlmProvider.Wrap(provider, sink: null);
        var ct = TestContext.Current.CancellationToken;

        var start = capture.Gauge(gauge);
        provider.SetChatResult(Billed(Answer(model), 0.5, "USD"));
        await metered.ChatAsync(Conversation(), cancellationToken: ct);
        var afterUsd = capture.Gauge(gauge);
        provider.SetChatResult(Billed(Answer(model), 0.25, "EUR"));
        await metered.ChatAsync(Conversation(), cancellationToken: ct);
        var afterEur = capture.Gauge(gauge);

        Assert.NotNull(start);
        Assert.NotNull(afterUsd);
        Assert.NotNull(afterEur);
        Assert.Equal(0.5, afterUsd.Value - start.Value, 6);
        Assert.Equal(afterUsd.Value, afterEur.Value, 6);
    }

    [Trait("BR", "CH-03")]
    [Fact]
    public async Task ShouldCarryProviderAndModelButNoId_OnEveryMeasurement()
    {
        const string model = "f5-metrics-tags-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(model));

        await MeteredLlmProvider.Wrap(provider, sink: null).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var seen = capture.For(model);
        Assert.Contains(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.Contains(seen, m => m.Instrument == "gen_ai.client.token.usage");
        Assert.Contains(seen, m => m.Instrument == "orkeon.llm.calls");
        foreach (var m in seen)
        {
            Assert.True(m.Tags.ContainsKey("gen_ai.provider.name"), m.Instrument);
            Assert.True(m.Tags.ContainsKey("gen_ai.operation.name"), m.Instrument);
            Assert.DoesNotContain(m.Tags.Keys, k => k == "id" || k.EndsWith(".id", StringComparison.Ordinal) || k.EndsWith("_id", StringComparison.Ordinal));
        }
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordADurationWithErrorType_WhenAStreamThrows()
    {
        const string model = "f5-metrics-stream-throws-model";
        using var capture = new Capture();
        var provider = new MockStreamingLlmProvider { Name = "broken-stream-vendor" };
        provider.SetStreamingChunks(["first"]);
        provider.FailGenerateStreamingAfter(1, new InvalidOperationException("stream broke"));
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink: null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in metered.GenerateStreamingAsync("p", LlmConfig.Create(model), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal("stream broke", thrown.Message);
        var seen = capture.For(model);
        var duration = Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.Equal("System.InvalidOperationException", duration.Tags["error.type"]);
        Assert.DoesNotContain(seen, m => m.Instrument == "gen_ai.client.token.usage");
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldRecordADurationWithErrorType_WhenTheResponseCarriesAnError()
    {
        const string model = "f5-metrics-error-response-model";
        using var capture = new Capture();
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(new LlmResponse { Content = string.Empty, Model = model, Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = "rate limited" } });

        await MeteredLlmProvider.Wrap(provider, sink: null).ChatAsync(Conversation(), LlmConfig.Create(model), TestContext.Current.CancellationToken);

        var seen = capture.For(model);
        var duration = Assert.Single(seen, m => m.Instrument == "gen_ai.client.operation.duration");
        Assert.False(string.IsNullOrEmpty(duration.Tags.GetValueOrDefault("error.type") as string));
        Assert.Equal(1, Assert.Single(seen, m => m.Instrument == "orkeon.llm.calls").Value);
        Assert.DoesNotContain(seen, m => m.Instrument == "gen_ai.client.token.usage");
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldKeepTheOriginalException_WhenDisposeAsyncThrowsAfterAStreamFailure()
    {
        var provider = new MockStreamingLlmProvider { Name = "dispose-stream-vendor" };
        provider.BreakStreamsAndDisposalWith(new InvalidOperationException("stream broke"), new ObjectDisposedException("enumerator"));
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink: null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in metered.GenerateStreamingAsync("p", LlmConfig.Create("f5-dispose-stream-model"), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal("stream broke", thrown.Message);
    }

    [Trait("BR", "CH-01")]
    [Fact]
    public async Task ShouldKeepTheOriginalException_WhenDisposeAsyncThrowsAfterAToolStreamFailure()
    {
        var provider = new MockStreamingLlmProvider { Name = "dispose-chat-stream-vendor" };
        provider.BreakStreamsAndDisposalWith(new InvalidOperationException("stream broke"), new ObjectDisposedException("enumerator"));
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink: null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in metered.ChatStreamingAsync(Conversation(), LlmConfig.Create("f5-dispose-chat-model"), TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal("stream broke", thrown.Message);
    }
}
