using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs.Base;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

public sealed class HttpLlmProviderBaseTests : IDisposable
{
    private readonly TestHttpMessageHandler _messageHandler;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmConfig _config;
    private readonly TestLogger<TestHttpLlmProvider> _logger;
    private readonly TestHttpLlmProvider _provider;

    public HttpLlmProviderBaseTests()
    {
        _messageHandler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(_messageHandler);
        _httpClientFactory = new TestHttpClientFactory(httpClient);
        _config = LlmConfig.Create(TestModelName, TestApiKey) with { MaxRetries = 0 }; // error tests pin mapping, not retry
        _logger = new TestLogger<TestHttpLlmProvider>();
        _provider = new TestHttpLlmProvider(_config, _httpClientFactory, _logger);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullConfig()
    {
        // Arrange
        LlmConfig? config = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TestHttpLlmProvider(config!, _httpClientFactory, _logger));
        Assert.Equal("config", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullHttpClientFactory()
    {
        // Arrange
        IHttpClientFactory? httpClientFactory = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TestHttpLlmProvider(_config, httpClientFactory!, _logger));
        Assert.Equal("httpClientFactory", exception.ParamName);
    }

    [Fact]
    public void ShouldUseNullLogger_WhenConstructorWithNullLogger()
    {
        // Arrange & Act
        using var provider = new TestHttpLlmProvider(_config, _httpClientFactory, null);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void ShouldReturnProviderName_WhenName()
    {
        // Act
        var name = _provider.Name;

        // Assert
        Assert.Equal("TestHttpLlmProvider", name);
    }

    [Fact]
    public async Task ShouldReturnResponse_WhenGenerateAsyncWithValidPrompt()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Generated response" }))
        });

        // Act
        var response = await _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Generated response", response.Content);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGenerateAsyncWithNullPrompt()
    {
        // Arrange
        string? prompt = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _provider.GenerateAsync(prompt!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnResponse_WhenChatAsyncWithValidMessages()
    {
        // Arrange
        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "You are a helpful assistant" },
            new LlmMessage { Role = "user", Content = "Hello" }
        };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Chat response" }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Chat response", response.Content);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenChatAsyncWithNullMessages()
    {
        // Arrange
        LlmMessage[]? messages = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _provider.ChatAsync(messages!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenChatAsyncWithEmptyMessages()
    {
        // Arrange
        var messages = Array.Empty<LlmMessage>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenValidateConnectionAsyncWithSuccessfulConnection()
    {
        // Arrange
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await _provider.ValidateConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenValidateConnectionAsyncWithFailedConnection()
    {
        // Arrange
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        // Act
        var result = await _provider.ValidateConnectionAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenValidateConnectionAsyncWithException()
    {
        // Arrange
        _messageHandler.SetupException(new HttpRequestException("Connection failed"));

        // Act
        var result = await _provider.ValidateConnectionAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldNotThrow_WhenDispose()
    {
        // Arrange
        using var provider = new TestHttpLlmProvider(_config, _httpClientFactory, _logger);

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => provider.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void ShouldNotThrow_WhenDisposeMultipleTimes()
    {
        // Arrange
        using var provider = new TestHttpLlmProvider(_config, _httpClientFactory, _logger);

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
        {
            provider.Dispose();
            provider.Dispose();
        });
        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldRespectCancellationToken_WhenGenerateAsyncWithCancellation()
    {
        // Arrange
        var prompt = TestPrompt;
        using var cts = new CancellationTokenSource();
        _messageHandler.SetupDelay(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _provider.GenerateAsync(prompt, null, cts.Token));
    }

    [Fact]
    public async Task ShouldRespectCancellationToken_WhenChatAsyncWithCancellation()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Hello" } };
        using var cts = new CancellationTokenSource();
        _messageHandler.SetupDelay(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _provider.ChatAsync(messages, null, cts.Token));
    }

    [Fact]
    public async Task ShouldThrowException_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server error")
        });

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowException_WhenChatAsyncWithHttpError()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Hello" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Bad request")
        });

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowException_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Invalid JSON")
        });

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(
            () => _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldIncludeApiKeyInHeaders_WhenGenerateAsync()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response" }))
        });

        // Act
        await _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var lastRequest = _messageHandler.LastRequest;
        Assert.NotNull(lastRequest);
        Assert.Contains(lastRequest.Headers, h => h.Key == "Authorization");
    }

    [Fact]
    public async Task ShouldSerializeMessagesCorrectly_WhenChatAsync()
    {
        // Arrange
        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "System message" },
            new LlmMessage { Role = "user", Content = "User message" },
            new LlmMessage { Role = "assistant", Content = "Assistant message" }
        };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response" }))
        });

        // Act
        await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var lastRequest = _messageHandler.LastRequest;
        Assert.NotNull(lastRequest);
        var requestContent = await lastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("System message", requestContent);
        Assert.Contains("User message", requestContent);
        Assert.Contains("Assistant message", requestContent);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGenerateAsyncWithEmptyPrompt()
    {
        // Arrange
        var prompt = "";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGenerateAsyncWithWhitespacePrompt()
    {
        // Arrange
        var prompt = "   ";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenGenerateAsyncWithVeryLongPrompt()
    {
        // Arrange
        var prompt = new string('a', 10000);
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response for long prompt" }))
        });

        // Act
        var response = await _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Response for long prompt", response.Content);
    }

    [Fact]
    public async Task ShouldWork_WhenChatAsyncWithSingleMessage()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Single message" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response" }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Response", response.Content);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenChatAsyncWithManyMessages()
    {
        // Arrange
        var messages = Enumerable.Range(0, 100).Select(i =>
            new LlmMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"Message {i}" }).ToArray();
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response for many messages" }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Response for many messages", response.Content);
    }

    [Fact]
    public async Task ShouldUseCustomSettings_WhenGenerateAsyncWithCustomConfig()
    {
        // Arrange
        var prompt = TestPrompt;
        var customConfig = LlmConfig.Create(CustomModelName, CustomApiKey);
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Custom response" }))
        });

        // Act
        var response = await _provider.GenerateAsync(prompt, customConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Custom response", response.Content);
    }

    [Fact]
    public async Task ShouldUseCustomSettings_WhenChatAsyncWithCustomConfig()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Test" } };
        var customConfig = LlmConfig.Create(CustomModelName, CustomApiKey);
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Custom response" }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, customConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Custom response", response.Content);
    }

    [Fact]
    public async Task ShouldThrowException_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Rate limit exceeded")
        });

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowException_WhenChatAsyncWithRateLimitError()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Test" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Rate limit exceeded")
        });

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowException_WhenGenerateAsyncWithUnauthorizedError()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Unauthorized")
        });

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowException_WhenChatAsyncWithUnauthorizedError()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Test" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Unauthorized")
        });

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnEmptyContent_WhenGenerateAsyncWithEmptyResponse()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "" }))
        });

        // Act
        var response = await _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("", response.Content);
    }

    [Fact]
    public async Task ShouldReturnEmptyContent_WhenChatAsyncWithEmptyResponse()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Test" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "" }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("", response.Content);
    }

    [Fact]
    public async Task ShouldReturnEmptyContent_WhenGenerateAsyncWithNullResponseContent()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { }))
        });

        // Act
        var response = await _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("", response.Content);
    }

    [Fact]
    public async Task ShouldReturnEmptyContent_WhenChatAsyncWithNullResponseContent()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Test" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("", response.Content);
    }

    [Fact]
    public async Task ShouldIncludeMetadataInResponse_WhenGenerateAsync()
    {
        // Arrange
        var prompt = TestPrompt;
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response" }))
        });

        // Act
        var response = await _provider.GenerateAsync(prompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Metadata);
        Assert.Equal(TestModelName, response.Metadata["model"]);
        Assert.Equal(10, response.Metadata["tokens_used"]);
        Assert.Equal(100, response.Metadata["response_time_ms"]);
    }

    [Fact]
    public async Task ShouldIncludeMetadataInResponse_WhenChatAsync()
    {
        // Arrange
        var messages = new[] { new LlmMessage { Role = "user", Content = "Test" } };
        _messageHandler.SetupResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { content = "Response" }))
        });

        // Act
        var response = await _provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response.Metadata);
        Assert.Equal(TestModelName, response.Metadata["model"]);
        Assert.Equal(10, response.Metadata["tokens_used"]);
        Assert.Equal(100, response.Metadata["response_time_ms"]);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenValidateConnectionAsyncWithNetworkError()
    {
        // Arrange
        _messageHandler.SetupException(new SocketException());

        // Act
        var result = await _provider.ValidateConnectionAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenValidateConnectionAsyncWithTimeoutError()
    {
        // Arrange
        _messageHandler.SetupException(new TaskCanceledException());

        // Act
        var result = await _provider.ValidateConnectionAsync();

        // Assert
        Assert.False(result);
    }

    public void Dispose()
    {
        _messageHandler.Dispose();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Test implementation of HttpLlmProviderBase
internal class TestHttpLlmProvider : HttpLlmProviderBase
{
    public override string Name => "TestHttpLlmProvider";

    public TestHttpLlmProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<TestHttpLlmProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    public override async Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentNullException(nameof(prompt));

        var httpClient = HttpClientFactory.CreateClient(Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.test.com/generate");
        request.Headers.Add("Authorization", $"Bearer {Config.ApiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { prompt = prompt }),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Request failed with status {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<TestResponse>(json, JsonOptions);

        return new LlmResponse
        {
            Content = result?.Content ?? string.Empty,
            Model = Config.Model,
            TokensUsed = 10,
            Metadata = new Dictionary<string, object>
            {
                ["model"] = Config.Model,
                ["tokens_used"] = 10,
                ["response_time_ms"] = 100
            }
        };
    }

    public override async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Length == 0)
            throw new ArgumentException("Messages cannot be empty", nameof(messages));

        var httpClient = HttpClientFactory.CreateClient(Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.test.com/chat");
        request.Headers.Add("Authorization", $"Bearer {Config.ApiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { messages = messages }),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Request failed with status {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<TestResponse>(json, JsonOptions);

        return new LlmResponse
        {
            Content = result?.Content ?? string.Empty,
            Model = Config.Model,
            TokensUsed = 10,
            Metadata = new Dictionary<string, object>
            {
                ["model"] = Config.Model,
                ["tokens_used"] = 10,
                ["response_time_ms"] = 100
            }
        };
    }

    public async Task<bool> ValidateConnectionAsync()
    {
        try
        {
            var httpClient = HttpClientFactory.CreateClient(Name);
            var response = await httpClient.GetAsync("https://api.test.com/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class TestResponse
    {
        public string Content { get; set; } = string.Empty;
    }
}

// Test HTTP client factory
internal class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _httpClient;

    public TestHttpClientFactory(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public HttpClient CreateClient(string name)
    {
        return _httpClient;
    }
}

// Test HTTP message handler
internal class TestHttpMessageHandler : HttpMessageHandler
{
    private HttpResponseMessage _response = new(HttpStatusCode.OK);
    private Exception? _exception;
    private TimeSpan? _delay;

    public HttpRequestMessage? LastRequest { get; private set; }

    public void SetupResponse(HttpResponseMessage response)
    {
        _response = response;
        _exception = null;
    }

    public void SetupException(Exception exception)
    {
        _exception = exception;
    }

    public void SetupDelay(TimeSpan delay)
    {
        _delay = delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Capture a clone: the sent request's content is disposed by the HTTP
        // pipeline after SendAsync, while tests inspect LastRequest afterwards.
        LastRequest = await CloneRequestAsync(request, cancellationToken);

        if (_delay.HasValue)
        {
            await Task.Delay(_delay.Value, cancellationToken);
        }

        if (_exception != null)
            throw _exception;

        return _response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content != null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            clone.Content = new StringContent(
                body,
                System.Text.Encoding.UTF8,
                request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _response.Dispose();
            LastRequest?.Dispose();
        }

        base.Dispose(disposing);
    }
}

// Test logger
internal class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _logs = [];

    public List<LogEntry> Logs => _logs;

    public bool HasLoggedError() => _logs.Any(l => l.LogLevel == Microsoft.Extensions.Logging.LogLevel.Error);
    public bool HasLoggedWarning() => _logs.Any(l => l.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning);
    public bool HasLoggedInformation() => _logs.Any(l => l.LogLevel == Microsoft.Extensions.Logging.LogLevel.Information);

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logs.Add(new LogEntry
        {
            LogLevel = logLevel,
            Message = formatter(state, exception),
            Exception = exception
        });
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}

internal class LogEntry
{
    public Microsoft.Extensions.Logging.LogLevel LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

#pragma warning restore CS0618
