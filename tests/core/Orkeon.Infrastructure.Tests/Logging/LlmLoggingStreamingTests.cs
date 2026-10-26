using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Polly;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.Logging;

public sealed class LlmLoggingStreamingTests
{
    private const string Chunk1 = "data: {\"delta\":\"Hel\"}\n\n";
    private const string Chunk2 = "data: {\"delta\":\"lo\"}\n\n";
    private const string RequestJson = """{"model":"gpt-4","stream":true,"messages":[]}""";

    [Fact]
    [Trait("BR", "BR-15")]
    public async Task ShouldHandTheFirstChunkOver_BeforeTheLoggedStreamEnds()
    {
        var token = TestContext.Current.CancellationToken;
        var logger = new RecordingExchangeLogger();
        var gated = new GatedStream(Encoding.UTF8.GetBytes(Chunk1), Encoding.UTF8.GetBytes(Chunk2));
        using var invoker = CreateInvoker(logger, null, gated);
        using var request = CreateRequest();
        HttpResponseMessage? response = null;

        try
        {
#pragma warning disable CA2025 // send is awaited or the gate is released in finally; the invoker outlives it.
            var send = invoker.SendAsync(request, token);
#pragma warning restore CA2025
            var winner = await Task.WhenAny(send, Task.Delay(Polling.DefaultTimeout, token));
            Assert.True(ReferenceEquals(winner, send), "SendAsync did not return while the stream was still open.");
            response = await send;

            var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[Chunk1.Length];
            var read = await stream.ReadAsync(buffer, token).AsTask().WaitAsync(Polling.DefaultTimeout, token);
            Assert.Equal(Chunk1, Encoding.UTF8.GetString(buffer, 0, read));
            Assert.Empty(logger.Records);

            gated.Release();
            using var rest = new MemoryStream();
            await stream.CopyToAsync(rest, token);
        }
        finally
        {
            gated.Release();
            response?.Dispose();
        }

        await Polling.WaitUntilAsync(() => logger.Records.Count >= 1);
        Assert.Single(logger.Records);
    }

    [Fact]
    [Trait("BR", "BR-15")]
    public async Task ShouldLogTheRebuiltAnswer_WhenAStreamedCallEnds()
    {
        var logger = new RecordingExchangeLogger();
        var gated = new GatedStream(Encoding.UTF8.GetBytes(Chunk1), Encoding.UTF8.GetBytes(Chunk2));
        gated.Release();
        using var invoker = CreateInvoker(logger, null, gated);
        using var request = CreateRequest();
        var capture = new LlmExchangeCapture();
        request.Options.Set(LlmExchangeOptions.Rebuilt, capture);

        var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        capture.Body = """{"answer":"Hello"}""";
        response.Dispose();
        await Polling.WaitUntilAsync(() => logger.Records.Count >= 1);

        var record = Assert.Single(logger.Records);
        Assert.Equal("""{"answer":"Hello"}""", record.ResponseBody);
        Assert.True(record.IsStreaming);
    }

    [Fact]
    [Trait("BR", "BR-15")]
    public async Task ShouldLogTheCapturedText_WhenAStreamIsCutShort()
    {
        var logger = new RecordingExchangeLogger();
        var gated = new GatedStream(Encoding.UTF8.GetBytes(Chunk1), Encoding.UTF8.GetBytes(Chunk2));
        using var invoker = CreateInvoker(logger, null, gated);
        using var request = CreateRequest();
        request.Options.Set(LlmExchangeOptions.Rebuilt, new LlmExchangeCapture());

        var token = TestContext.Current.CancellationToken;
        HttpResponseMessage? response = null;
        try
        {
#pragma warning disable CA2025 // send is awaited or the gate is released in finally; the invoker outlives it.
            var send = invoker.SendAsync(request, token);
#pragma warning restore CA2025
            var winner = await Task.WhenAny(send, Task.Delay(Polling.DefaultTimeout, token));
            Assert.True(ReferenceEquals(winner, send), "SendAsync did not return while the stream was still open.");
            response = await send;
            var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[Chunk1.Length];
            var read = await stream.ReadAsync(buffer, token).AsTask().WaitAsync(Polling.DefaultTimeout, token);
            Assert.Equal(Chunk1.Length, read);
        }
        finally
        {
            response?.Dispose();
            gated.Release();
        }

        await Polling.WaitUntilAsync(() => logger.Records.Count >= 1);

        var record = Assert.Single(logger.Records);
        Assert.Contains("\"delta\":\"Hel\"", record.ResponseBody, StringComparison.Ordinal);
        Assert.True(record.IsStreaming);
    }

    [Fact]
    [Trait("BR", "BR-15")]
    public async Task ShouldStillSkipStreamedExchanges_WhenLogStreamingExchangesIsOff()
    {
        var logger = new RecordingExchangeLogger();
        var gated = new GatedStream(Encoding.UTF8.GetBytes(Chunk1), Encoding.UTF8.GetBytes(Chunk2));
        gated.Release();
        using var invoker = CreateInvoker(logger, new LlmLoggingOptions { LogStreamingExchanges = false }, gated);
        using var request = CreateRequest();

        var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.Dispose();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.Empty(logger.Records);
    }

    [Fact]
    [Trait("BR", "BR-15")]
    public async Task ShouldLogTheProvidersRebuiltAnswer_WhenAProviderStreamsThroughTheHandler()
    {
        var token = TestContext.Current.CancellationToken;
        var sse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"content":"Hello "}}]}""",
            """data: {"choices":[{"delta":{"content":"world"}}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"lookup_weather","arguments":"{\"city\":"}}]}}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Paris\"}"}}]},"finish_reason":"tool_calls"}]}""",
            """data: {"choices":[],"usage":{"total_tokens":4242,"prompt_tokens":3131,"completion_tokens":1111}}""",
            "data: [DONE]") + "\n\n";
        var logger = new RecordingExchangeLogger();
        using var inner = new MockHttpMessageHandler();
        inner.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse))),
        });
#pragma warning disable CA2000 // Owned by the client, which is disposed.
        using var client = new HttpClient(
            new LlmLoggingDelegatingHandler(logger, NullLogger<LlmLoggingDelegatingHandler>.Instance, null) { InnerHandler = inner });
#pragma warning restore CA2000
        using var factory = new MockHttpClientFactory();
        factory.SetDefaultClient(client);
        var tool = new ToolSchema("lookup_weather", "Weather of a city",
            new Dictionary<string, ParameterSchema> { ["city"] = new("string", "The city", true) });
#pragma warning disable CS0618 // LlmConfig.ApiKey
        var config = LlmConfig.Create("deepseek-chat") with { MaxRetries = 0, ApiKey = "sk-test", Tools = [tool] };
#pragma warning restore CS0618
        using var provider = new DeepSeekLlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), NullLogger<DeepSeekLlmProvider>.Instance);

        await foreach (var _ in provider.ChatStreamingAsync([LlmMessage.User("weather?")], cancellationToken: token).WithCancellation(token))
        {
        }

        await Polling.WaitUntilAsync(() => logger.Records.Count >= 1);
        var record = Assert.Single(logger.Records);
        var body = record.ResponseBody!;
        Assert.False(body.StartsWith("data:", StringComparison.Ordinal), "The raw event text was logged.");
        Assert.Contains("Hello world", body, StringComparison.Ordinal);
        Assert.Contains("lookup_weather", body, StringComparison.Ordinal);
        Assert.Contains("4242", body, StringComparison.Ordinal);
        Assert.Contains("3131", body, StringComparison.Ordinal);
        Assert.Contains("1111", body, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("content").ValueKind);
        Assert.Equal("Hello world", root.GetProperty("content").GetString());
        Assert.True(root.TryGetProperty("tool_calls", out var calls));
        Assert.Equal(JsonValueKind.Array, calls.ValueKind);
        Assert.Contains("lookup_weather", calls.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(4242, root.GetProperty("usage").GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-197")]
    public async Task ShouldLogTheCacheCounts_WhenADeepSeekStreamReportsThem()
    {
        var usage = await LogStreamedUsageAsync(
            """{"prompt_tokens":1000,"completion_tokens":50,"total_tokens":1050,"prompt_cache_hit_tokens":896,"prompt_cache_miss_tokens":104}""");

        var hit = usage.GetProperty("prompt_cache_hit_tokens").GetInt32();
        var miss = usage.GetProperty("prompt_cache_miss_tokens").GetInt32();
        Assert.Equal(896, hit);
        Assert.Equal(104, miss);
        Assert.Equal(usage.GetProperty("prompt_tokens").GetInt32(), hit + miss);
        Assert.Equal(1050, usage.GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-197")]
    public async Task ShouldLogTheTokenDetails_WhenAnOpenAiShapedStreamReportsThem()
    {
        var usage = await LogStreamedUsageAsync(
            """{"prompt_tokens":800,"completion_tokens":300,"total_tokens":1100,"prompt_tokens_details":{"cached_tokens":640},"completion_tokens_details":{"reasoning_tokens":220}}""");

        Assert.Equal(640, usage.GetProperty("prompt_tokens_details").GetProperty("cached_tokens").GetInt32());
        Assert.Equal(220, usage.GetProperty("completion_tokens_details").GetProperty("reasoning_tokens").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-197")]
    public async Task ShouldLogTheCost_WhenTheStreamReportsOne()
    {
        var usage = await LogStreamedUsageAsync(
            """{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15,"cost":0.00042}""");

        Assert.Equal(0.00042, usage.GetProperty("cost").GetDouble(), 10);
    }

    [Fact]
    [Trait("BR", "CH-197")]
    public async Task ShouldLogExactlyTheThreeTotals_WhenTheStreamReportsNothingElse()
    {
        var usage = await LogStreamedUsageAsync(
            """{"total_tokens":15,"prompt_tokens":10,"completion_tokens":5}""");

        var names = usage.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["completion_tokens", "prompt_tokens", "total_tokens"], names);
        Assert.Equal(15, usage.GetProperty("total_tokens").GetInt32());
        Assert.Equal(10, usage.GetProperty("prompt_tokens").GetInt32());
        Assert.Equal(5, usage.GetProperty("completion_tokens").GetInt32());
    }

    /// <summary>Streams one answer whose final chunk carries <paramref name="usageJson"/>; returns the logged usage.</summary>
    private static async Task<JsonElement> LogStreamedUsageAsync(string usageJson)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var sse = string.Join("\n\n",
            """data: {"choices":[{"delta":{"content":"Hi"},"finish_reason":"stop"}]}""",
            "data: {\"choices\":[],\"usage\":" + usageJson + "}",
            "data: [DONE]") + "\n\n";
        var logger = new RecordingExchangeLogger();
        using var inner = new MockHttpMessageHandler();
        inner.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse))),
        });
#pragma warning disable CA2000 // Owned by the client, which is disposed.
        using var client = new HttpClient(
            new LlmLoggingDelegatingHandler(logger, NullLogger<LlmLoggingDelegatingHandler>.Instance, null) { InnerHandler = inner });
#pragma warning restore CA2000
        using var factory = new MockHttpClientFactory();
        factory.SetDefaultClient(client);
        // Any non-empty placeholder takes the streamed path.
#pragma warning disable CS0618 // LlmConfig.ApiKey
        var config = LlmConfig.Create("deepseek-chat") with { MaxRetries = 0, ApiKey = nameof(usageJson) };
#pragma warning restore CS0618
        using var provider = new DeepSeekLlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), NullLogger<DeepSeekLlmProvider>.Instance);

        await foreach (var _ in provider.ChatStreamingAsync([LlmMessage.User("hi")], cancellationToken: cancellation).WithCancellation(cancellation))
        {
        }

        await Polling.WaitUntilAsync(() => logger.Records.Count >= 1);
        var record = Assert.Single(logger.Records);
        using var doc = JsonDocument.Parse(record.ResponseBody!);
        return doc.RootElement.GetProperty("usage").Clone();
    }

    [Fact]
    [Trait("BR", "BR-15")]
    public async Task ShouldLogTheToolCalls_WhenAnAnthropicStreamCallsATool()
    {
        var ct = TestContext.Current.CancellationToken;
        var sse = string.Join("\n\n",
            """data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}""",
            """data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"lookup_weather","input":{}}}""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"city\":"}}""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"\"Paris\"}"}}""",
            """data: {"type":"content_block_stop","index":0}""",
            """data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":7}}""",
            """data: {"type":"message_stop"}""",
            "") + "\n";
        var logger = new RecordingExchangeLogger();
        using var inner = new MockHttpMessageHandler();
        inner.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse))),
        });
#pragma warning disable CA2000 // Owned by the client, which is disposed.
        using var client = new HttpClient(
            new LlmLoggingDelegatingHandler(logger, NullLogger<LlmLoggingDelegatingHandler>.Instance, null) { InnerHandler = inner });
#pragma warning restore CA2000
        using var factory = new MockHttpClientFactory();
        factory.SetDefaultClient(client);
        var tool = new ToolSchema("lookup_weather", "Weather of a city",
            new Dictionary<string, ParameterSchema> { ["city"] = new("string", "The city", true) });
        var config = LlmConfig.Create("claude-sonnet-5", Orkeon.Tests.Shared.Constants.TestLlmConstants.TestApiKey)
            with { MaxRetries = 0, Tools = [tool] };
        using var provider = new AnthropicLlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), NullLogger<AnthropicLlmProvider>.Instance);

        await foreach (var _ in provider.ChatStreamingAsync([LlmMessage.User("weather?")], cancellationToken: ct).WithCancellation(ct))
        {
        }

        await Polling.WaitUntilAsync(() => logger.Records.Count >= 1);
        var record = Assert.Single(logger.Records);
        using var doc = JsonDocument.Parse(record.ResponseBody!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("tool_calls", out var calls));
        Assert.Equal(JsonValueKind.Array, calls.ValueKind);
        Assert.Contains("lookup_weather", calls.GetRawText(), StringComparison.Ordinal);
        Assert.True(root.TryGetProperty("content", out _));
        Assert.Equal(7, root.GetProperty("usage").GetProperty("completion_tokens").GetInt32());
    }

    private static HttpRequestMessage CreateRequest() =>
        new(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(RequestJson, Encoding.UTF8, "application/json"),
        };

    private static HttpMessageInvoker CreateInvoker(RecordingExchangeLogger logger, LlmLoggingOptions? options, GatedStream body)
    {
#pragma warning disable CA2000 // Owned by the invoker, which the caller disposes.
        return new HttpMessageInvoker(
            new LlmLoggingDelegatingHandler(logger, NullLogger<LlmLoggingDelegatingHandler>.Instance, options)
            {
                InnerHandler = new StreamServingHandler(body),
            },
            disposeHandler: true);
#pragma warning restore CA2000
    }

    private sealed class StreamServingHandler(Stream body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>Serves the first chunk at once, blocks until released, then serves the rest and ends.</summary>
    private sealed class GatedStream(byte[] first, byte[] rest) : Stream
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stage;

        public void Release() => _gate.TrySetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_stage == 0)
            {
                _stage = 1;
                first.CopyTo(buffer);
                return first.Length;
            }

            if (_stage == 1)
            {
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                _stage = 2;
                rest.CopyTo(buffer);
                return rest.Length;
            }

            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingExchangeLogger : ILlmExchangeLogger
    {
        private readonly List<LlmExchangeRecord> _records = [];

        public IReadOnlyList<LlmExchangeRecord> Records
        {
            get { lock (_records) { return [.. _records]; } }
        }

        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
        {
            lock (_records) { _records.Add(exchange); }
            return Task.CompletedTask;
        }
    }
}
