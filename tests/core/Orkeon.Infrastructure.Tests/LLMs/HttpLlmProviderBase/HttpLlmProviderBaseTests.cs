using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs.Base;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public sealed class HttpLlmProviderBaseTests : IDisposable
{
    #region Test Doubles

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _logEntries = [];

        public List<LogEntry> LogEntries => _logEntries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logEntries.Add(new LogEntry
            {
                LogLevel = logLevel,
                Message = formatter(state, exception),
                Exception = exception
            });
        }

        public bool HasLoggedError(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Error && e.Message.Contains(containsText));

        public bool HasLoggedInfo(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Information && e.Message.Contains(containsText));

        public bool HasLoggedDebug(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Debug && e.Message.Contains(containsText));

        public class LogEntry
        {
            public LogLevel LogLevel { get; init; }
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions s_snakeCaseOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        private readonly Queue<HttpResponseMessage> _responses = new();
        private readonly List<HttpRequestMessage> _capturedRequests = [];

        public List<HttpRequestMessage> CapturedRequests => _capturedRequests;

        public void QueueResponse(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response is enqueued for a later request; leftovers are disposed by this handler's Dispose.")]
        public void QueueJsonResponse(object content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var json = JsonSerializer.Serialize(content, s_snakeCaseOptions);

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _capturedRequests.Add(CloneRequest(request));

            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("No response configured")
                };
            }

            await System.Threading.Tasks.Task.Delay(10, cancellationToken); // Simulate network delay
            return _responses.Dequeue();
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
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
                clone.Content = new StringContent(
                    request.Content.ReadAsStringAsync().Result,
                    Encoding.UTF8,
                    request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            return clone;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (_responses.Count > 0)
                {
                    _responses.Dequeue().Dispose();
                }

                foreach (var request in _capturedRequests)
                {
                    request.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly TestHttpMessageHandler _handler;

        public TestHttpClientFactory(TestHttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, false)
            {
                BaseAddress = new Uri("https://api.test.com/")
            };
        }
    }

    private class TestLlmProvider : HttpLlmProviderBase
    {
        public override string Name => "TestProvider";

        public TestLlmProvider(
            LlmConfig config,
            IHttpClientFactory httpClientFactory,
            ILogger? logger = null)
            : base(config, httpClientFactory, logger)
        {
        }

        public override async Task<LlmResponse> GenerateAsync(
            string prompt,
            LlmConfig? config = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveConfig = config ?? Config;

            var requestBody = new
            {
                model = effectiveConfig.Model,
                prompt = prompt,
                max_tokens = effectiveConfig.MaxTokens,
                temperature = (float)effectiveConfig.Temperature
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/completions")
            {
                Content = new StringContent(SerializeToJson(requestBody), Encoding.UTF8, "application/json")
            };

            var response = await ExecuteHttpRequestAsync(request, effectiveConfig, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return await CreateErrorResponseAsync(response, cancellationToken);
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = DeserializeFromJson<TestApiResponse>(jsonContent);

            return new LlmResponse
            {
                Content = apiResponse?.Text ?? string.Empty,
                TokensUsed = apiResponse?.Usage?.TotalTokens ?? 0,
                Model = effectiveConfig.Model,
                Metadata = new Dictionary<string, object>
                {
                    ["provider"] = Name,
                    ["prompt_tokens"] = apiResponse?.Usage?.PromptTokens ?? 0,
                    ["completion_tokens"] = apiResponse?.Usage?.CompletionTokens ?? 0
                }
            };
        }

        protected override void ConfigureHttpClient(HttpClient client, LlmConfig config)
        {
            client.DefaultRequestHeaders.Add("X-Provider", "Test");
            if (config.CustomParameters.TryGetValue("OrganizationId", out var orgId) && orgId != null)
            {
                client.DefaultRequestHeaders.Add("X-Organization", orgId.ToString());
            }
        }

        // Expose protected methods for testing
        public new HttpClient CreateHttpClient(LlmConfig? config = null) => base.CreateHttpClient(config);
        public new string ConvertMessagesToPrompt(LlmMessage[] messages) => base.ConvertMessagesToPrompt(messages);
        public new string SerializeToJson(object obj) => base.SerializeToJson(obj);
        public new T? DeserializeFromJson<T>(string json) => base.DeserializeFromJson<T>(json);
        public new async Task<LlmResponse> CreateErrorResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
            => await base.CreateErrorResponseAsync(response, cancellationToken);
    }

    private class TestApiResponse
    {
        public string? Text { get; set; }
        public TestUsage? Usage { get; set; }
    }

    private class TestUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    #endregion

    private readonly TestLogger<TestLlmProvider> _logger;
    private readonly TestHttpMessageHandler _handler;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmConfig _defaultConfig;

    public HttpLlmProviderBaseTests()
    {
        _logger = new TestLogger<TestLlmProvider>();
        _handler = new TestHttpMessageHandler();
        _httpClientFactory = new TestHttpClientFactory(_handler);
        _defaultConfig = LlmConfig.Default() with
        {
            Model = TestModelName,
            ApiKey = TestApiKey,
            Temperature = 0.7,
            MaxTokens = 100,
            TimeoutSeconds = 30,
            MaxRetries = 0, // error tests pin mapping, not retry — the default budget would backoff for minutes
            CustomParameters = new Dictionary<string, object> { ["OrganizationId"] = "test-org" }
        };
    }

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructorWithValidConfig()
    {
        // Arrange & Act
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Assert
        Assert.Equal("TestProvider", provider.Name);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullConfig()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestLlmProvider(null!, _httpClientFactory, _logger));
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullHttpClientFactory()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestLlmProvider(_defaultConfig, null!, _logger));
    }

    [Fact]
    public void ShouldNotThrow_WhenConstructorWithNullLogger()
    {
        // Arrange & Act
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, null);

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region GenerateAsync Tests

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithSuccessfulResponse()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var apiResponse = new TestApiResponse
        {
            Text = "Generated response",
            Usage = new TestUsage
            {
                PromptTokens = 10,
                CompletionTokens = 20,
                TotalTokens = 30
            }
        };
        _handler.QueueJsonResponse(apiResponse);

        // Act
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Generated response", response.Content);
        Assert.Equal(30, response.TokensUsed);
        Assert.Equal(TestModelName, response.Model);
        Assert.Equal("TestProvider", response.Metadata["provider"]);
        Assert.Equal(10, response.Metadata["prompt_tokens"]);
        Assert.Equal(20, response.Metadata["completion_tokens"]);
    }

    [Fact]
    public async Task ShouldUseOverride_WhenGenerateAsyncWithOverrideConfig()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var overrideConfig = LlmConfig.Default() with
        {
            Model = "override-model",
            ApiKey = "override-key",
            Temperature = 0.9,
            MaxTokens = 200,
            TimeoutSeconds = 60
        };

        _handler.QueueJsonResponse(new TestApiResponse { Text = "Response" });

        // Act
        var response = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("override-model", response.Model);
        Assert.Single(_handler.CapturedRequests);

        var requestContent = await _handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"model\":\"override-model\"", requestContent);
        Assert.Contains("\"max_tokens\":200", requestContent);
        // Check for temperature with flexible formatting
        Assert.True(requestContent.Contains("\"temperature\":0.9") || requestContent.Contains("\"temperature\":0.9000"),
            $"Expected temperature 0.9 in request content: {requestContent}");
    }

    [Fact]
    public async Task ShouldReturnErrorInfo_WhenGenerateAsyncWithErrorResponse()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        using var errorResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"Invalid request\"}")
        };
        _handler.QueueResponse(errorResponse);

        // Act
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(response.Content);
        Assert.Equal(0, response.TokensUsed);
        Assert.Equal(400, response.Metadata["http_status_code"]);
        Assert.Contains("Invalid request", response.Metadata["error_content"].ToString());
        Assert.True(_logger.HasLoggedError("LLM HTTP error"));
    }

    [Fact]
    public async Task ShouldCancel_WhenGenerateAsyncWithCancellation()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GenerateAsync(TestPrompt, null, cts.Token));
    }

    #endregion

    #region ChatAsync Tests

    [Fact]
    public async Task ShouldConvertToPrompt_WhenChatAsyncWithMessages()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "You are helpful" },
            new LlmMessage { Role = "user", Content = "Hello" },
            new LlmMessage { Role = "assistant", Content = "Hi there!" },
            new LlmMessage { Role = "user", Content = "How are you?" }
        };

        _handler.QueueJsonResponse(new TestApiResponse { Text = "I'm doing well!" });

        // Act
        var response = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("I'm doing well!", response.Content);

        // Verify the prompt was converted correctly
        var requestContent = await _handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("system: You are helpful", requestContent);
        Assert.Contains("user: Hello", requestContent);
        Assert.Contains("assistant: Hi there!", requestContent);
        Assert.Contains("user: How are you?", requestContent);
    }

    [Fact]
    public async Task ShouldSendEmptyPrompt_WhenChatAsyncWithEmptyMessages()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        _handler.QueueJsonResponse(new TestApiResponse { Text = "Response" });

        // Act
        var response = await provider.ChatAsync([], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var requestContent = await _handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"prompt\":\"\"", requestContent);
    }

    [Fact]
    public async Task ShouldSendEmptyPrompt_WhenChatAsyncWithNullMessages()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        _handler.QueueJsonResponse(new TestApiResponse { Text = "Response" });

        // Act
        var response = await provider.ChatAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var requestContent = await _handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"prompt\":\"\"", requestContent);
    }

    #endregion

    #region HttpClient Configuration Tests

    [Fact]
    public void ShouldSetAuthorizationHeader_WhenCreateHttpClientWithApiKey()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Act
        using var client = provider.CreateHttpClient();

        // Assert
        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization.Scheme);
        Assert.Equal(TestApiKey, client.DefaultRequestHeaders.Authorization.Parameter);
    }

    [Fact]
    public void ShouldSetCustomHeader_WhenCreateHttpClientWithOrganizationId()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Act
        using var client = provider.CreateHttpClient();

        // Assert
        Assert.Contains("X-Provider", client.DefaultRequestHeaders.Select(h => h.Key));
        Assert.Contains("X-Organization", client.DefaultRequestHeaders.Select(h => h.Key));
        Assert.Equal("test-org", client.DefaultRequestHeaders.GetValues("X-Organization").First());
    }

    [Fact]
    public void ShouldSetTimeout_WhenCreateHttpClientWithTimeout()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Act
        using var client = provider.CreateHttpClient();

        // Assert
        Assert.Equal(TimeoutQuick, client.Timeout);
    }

    [Fact]
    public void ShouldUseOverride_WhenCreateHttpClientWithOverrideConfig()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var overrideConfig = LlmConfig.Default() with
        {
            ApiKey = "override-key",
            TimeoutSeconds = 60
        };

        // Act
        using var client = provider.CreateHttpClient(overrideConfig);

        // Assert
        Assert.Equal("override-key", client.DefaultRequestHeaders.Authorization?.Parameter);
        Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
    }

    #endregion

    #region Utility Method Tests

    [Fact]
    public void ShouldFormatCorrectly_WhenConvertMessagesToPrompt()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var messages = new[]
        {
            new LlmMessage { Role = "user", Content = "Hello" },
            new LlmMessage { Role = "assistant", Content = "Hi!" }
        };

        // Act
        var prompt = provider.ConvertMessagesToPrompt(messages);

        // Assert
        Assert.Equal("user: Hello\nassistant: Hi!", prompt);
    }

    [Fact]
    public void ShouldUseSnakeCaseNaming_WhenSerializeToJson()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var obj = new { MaxTokens = 100, TemperatureValue = 0.7 };

        // Act
        var json = provider.SerializeToJson(obj);

        // Assert
        Assert.Contains("\"max_tokens\":100", json);
        Assert.Contains("\"temperature_value\":0.7", json);
    }

    [Fact]
    public void ShouldDeserialize_WhenDeserializeFromJsonWithValidJson()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var json = "{\"prompt_tokens\":10,\"completion_tokens\":20,\"total_tokens\":30}";

        // Act
        var result = provider.DeserializeFromJson<TestUsage>(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.PromptTokens);
        Assert.Equal(20, result.CompletionTokens);
        Assert.Equal(30, result.TotalTokens);
    }

    [Fact]
    public void ShouldReturnNull_WhenDeserializeFromJsonWithInvalidJson()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var json = "invalid json";

        // Act
        var result = provider.DeserializeFromJson<TestUsage>(json);

        // Assert
        Assert.Null(result);
        Assert.True(_logger.HasLoggedError("Failed to deserialize JSON"));
    }

    [Fact]
    public void ShouldReturnNull_WhenDeserializeFromJsonWithEmptyString()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Act
        var result = provider.DeserializeFromJson<TestUsage>("");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region CreateErrorResponseAsync Tests

    [Fact]
    public async Task ShouldIncludeErrorDetails_WhenCreateErrorResponseAsync()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        using var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"Server error\",\"code\":\"500\"}")
        };

        // Act
        var response = await provider.CreateErrorResponseAsync(httpResponse, CancellationToken.None);

        // Assert
        Assert.Empty(response.Content);
        Assert.Equal(0, response.TokensUsed);
        Assert.Equal(TestModelName, response.Model);
        Assert.Equal(500, response.Metadata["http_status_code"]);
        Assert.Contains("Server error", response.Metadata["error_content"].ToString());
        Assert.Equal("TestProvider", response.Metadata["provider"]);
        Assert.True(_logger.HasLoggedError("LLM HTTP error: InternalServerError"));
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void ShouldNotThrow_WhenDispose()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Act & Assert
        var exception = Record.Exception(() => provider.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void ShouldNotThrow_WhenDisposeCalledMultipleTimes()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            provider.Dispose();
            provider.Dispose();
            provider.Dispose();
        });
        Assert.Null(exception);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithNullResponseContent()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        _handler.QueueJsonResponse(new TestApiResponse { Text = null });

        // Act
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(response.Content);
    }

    [Fact]
    public async Task ShouldDefaultToZero_WhenGenerateAsyncWithMissingUsageData()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        _handler.QueueJsonResponse(new TestApiResponse { Text = "Response", Usage = null });

        // Act
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, response.TokensUsed);
        Assert.Equal(0, response.Metadata["prompt_tokens"]);
        Assert.Equal(0, response.Metadata["completion_tokens"]);
    }

    [Fact]
    public async Task ShouldHandle_WhenGenerateAsyncWithLargePrompt()
    {
        // Arrange
        using var provider = new TestLlmProvider(_defaultConfig, _httpClientFactory, _logger);
        var largePrompt = new string('X', 10000);
        _handler.QueueJsonResponse(new TestApiResponse { Text = "Response" });

        // Act
        var response = await provider.GenerateAsync(largePrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response", response.Content);
        var requestContent = await _handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(largePrompt, requestContent);
    }

    #endregion

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }
}

#pragma warning restore CS0618
