using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.Logging;

public sealed class LlmLoggingDelegatingHandlerTests : IDisposable
{
    private readonly StubExchangeLogger _logger = new();
    private readonly LlmLoggingDelegatingHandler _handler;
    private readonly HttpMessageInvoker _invoker;
    private readonly FakeHttpMessageHandler _innerHandler;

    public LlmLoggingDelegatingHandlerTests()
    {
        _innerHandler = new FakeHttpMessageHandler();
        _handler = new LlmLoggingDelegatingHandler(
            _logger,
            NullLogger<LlmLoggingDelegatingHandler>.Instance)
        {
            InnerHandler = _innerHandler
        };
        _invoker = new HttpMessageInvoker(_handler);
    }

    [Fact]
    public async Task SendAsync_CapturesRequestAndResponseDetails()
    {
        var requestPayload = """{"model":"gpt-4","messages":[{"role":"user","content":"Hello"}]}""";
        var responsePayload = """{"choices":[{"message":{"content":"Hi!"}}],"usage":{"total_tokens":10}}""";
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responsePayload, Encoding.UTF8, "application/json")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(requestPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", "Bearer sk-test-key-1234567890abcdef");

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        Assert.Equal("openai", captured!.Provider);
        Assert.Equal("POST", captured.HttpMethod);
        Assert.Contains("chat/completions", captured.RequestUrl!.ToString(), StringComparison.Ordinal);
        Assert.Equal(200, captured.StatusCode);
        Assert.True(captured.IsSuccess);
        Assert.Equal("gpt-4", captured.Model);
        Assert.False(captured.IsStreaming);
        Assert.True(captured.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task SendAsync_SanitizesAuthorizationHeader()
    {
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", "Bearer sk-proj-abc123def456ghi789jkl012mno345");

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        var authValues = captured!.RequestHeaders
            .Where(h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value);
        foreach (var val in authValues)
        {
            Assert.DoesNotContain("sk-proj-abc123def456ghi789jkl012mno345", val);
            Assert.Contains("REDACTED", val);
        }
    }

    [Fact]
    public async Task SendAsync_RedactsAzureApiKeyHeader_ByName()
    {
        // Azure OpenAI sends a bare 32-char hex api-key with no sk-/Bearer prefix:
        // it is only redactable by header name, not by value pattern.
        const string azureApiKey = "0123456789abcdef0123456789abcdef";
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://my-resource.openai.azure.com/openai/deployments/gpt-4/chat/completions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", azureApiKey);

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        var apiKeyValues = captured!.RequestHeaders
            .Where(h => h.Key.Equals("api-key", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();
        Assert.NotEmpty(apiKeyValues);
        foreach (var val in apiKeyValues)
        {
            Assert.DoesNotContain(azureApiKey, val);
            Assert.Contains("REDACTED", val);
        }
    }

    [Fact]
    public async Task SendAsync_SanitizesSecretInRequestBody()
    {
        const string secret = "sk-abcdefghij1234567890abcdef";
        var payload = $$"""{"model":"gpt-4","api_key":"{{secret}}","messages":[]}""";
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        Assert.DoesNotContain(secret, captured!.RequestBody);
        Assert.Contains("REDACTED", captured.RequestBody);
    }

    [Fact]
    public async Task SendAsync_SanitizesSecretInResponseBody()
    {
        const string secret = "sk-ant-abcdefghij1234567890abcdef";
        var responsePayload = $$"""{"leaked_key":"{{secret}}"}""";
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responsePayload, Encoding.UTF8, "application/json")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        Assert.DoesNotContain(secret, captured!.ResponseBody);
        Assert.Contains("REDACTED", captured.ResponseBody);
    }

    [Fact]
    public async Task SendAsync_CapturesErrorResponse()
    {
        var errorBody = """{"error":{"message":"Rate limit exceeded","type":"rate_limit_error"}}""";
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent("""{"model":"claude-3-5-sonnet-20241022"}""", Encoding.UTF8, "application/json")
        };

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        Assert.Equal("anthropic", captured!.Provider);
        Assert.Equal(429, captured.StatusCode);
        Assert.False(captured.IsSuccess);
        Assert.NotNull(captured.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_DetectsStreamingRequests()
    {
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        var payload = """{"model":"gpt-4","stream":true,"messages":[]}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        await _invoker.SendAsync(request, CancellationToken.None);
        await Polling.WaitUntilAsync(() => _logger.Captured is not null);

        var captured = _logger.Captured;
        Assert.NotNull(captured);
        Assert.True(captured!.IsStreaming);
    }

    [Fact]
    public async Task SendAsync_LoggingFailureDoesNotPropagateToCallers()
    {
        _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

        _logger.ThrowOnLog = new IOException("Disk full");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await _invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_IdentifiesProviderFromUrl()
    {
        var testCases = new Dictionary<string, string>
        {
            ["https://api.openai.com/v1/chat/completions"] = "openai",
            ["https://api.anthropic.com/v1/messages"] = "anthropic",
            ["https://api.groq.com/openai/v1/chat/completions"] = "groq",
            ["http://localhost:11434/api/generate"] = "ollama",
            ["https://my-resource.openai.azure.com/openai/deployments/gpt-4/chat/completions"] = "azure-openai"
        };

        foreach (var (url, expectedProvider) in testCases)
        {
            _innerHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
            _logger.Reset();

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            await _invoker.SendAsync(request, CancellationToken.None);
            await Polling.WaitUntilAsync(() => _logger.Captured is not null);

            Assert.NotNull(_logger.Captured);
            Assert.Equal(expectedProvider, _logger.Captured!.Provider);
        }
    }

    /// <summary>Hand-rolled <see cref="ILlmExchangeLogger"/> stub.</summary>
    private sealed class StubExchangeLogger : ILlmExchangeLogger
    {
        public LlmExchangeRecord? Captured { get; private set; }
        public Exception? ThrowOnLog { get; set; }

        public void Reset() { Captured = null; ThrowOnLog = null; }

        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
        {
            Captured = exchange;
            return ThrowOnLog is null ? Task.CompletedTask : Task.FromException(ThrowOnLog);
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private HttpResponseMessage _response = new(HttpStatusCode.OK);

        public void SetResponse(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public void Dispose()
    {
        _handler.Dispose();
        _innerHandler.Dispose();
        _invoker.Dispose();
        GC.SuppressFinalize(this);
    }
}
