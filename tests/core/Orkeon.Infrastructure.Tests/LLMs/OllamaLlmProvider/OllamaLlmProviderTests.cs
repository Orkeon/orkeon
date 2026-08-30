using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public class OllamaLlmProviderTests
{
    #region Test Doubles

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _content = "";
        private readonly List<HttpRequestMessage> _requests = [];
        private readonly List<string> _requestContents = [];
        private Exception? _exceptionToThrow;
        private int _delayMs;

        public List<HttpRequestMessage> Requests => _requests;
        public List<string> RequestContents => _requestContents;

        public void SetupResponse(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        public void SetupException(Exception exception)
        {
            _exceptionToThrow = exception;
        }

        public void SetupDelay(int delayMs)
        {
            _delayMs = delayMs;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);

            // Preserve request content before it gets disposed
            if (request.Content != null)
            {
                var requestContent = await request.Content.ReadAsStringAsync(cancellationToken);
                _requestContents.Add(requestContent);
            }
            else
            {
                _requestContents.Add(string.Empty);
            }

            if (_delayMs > 0)
            {
                await System.Threading.Tasks.Task.Delay(_delayMs, cancellationToken);
            }

            if (_exceptionToThrow != null)
            {
                throw _exceptionToThrow;
            }

            // Create a new response and content each time to avoid disposal issues
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };

            // Load content into buffer to make it accessible even after HttpClient disposal
            await response.Content.LoadIntoBufferAsync(cancellationToken);

            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var request in _requests)
                {
                    request.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, HttpClient> _clients = [];

        public void RegisterClient(string name, HttpClient client)
        {
            _clients[name] = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _clients.TryGetValue(name, out var client)
                ? client
                : throw new InvalidOperationException($"No client registered for {name}");
        }
    }

    private class TestLogger : ILogger<OllamaLlmProvider>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }

        public bool HasLoggedError(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Error]") && m.Contains(partialMessage));
        }

        public bool HasLoggedWarning(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Warning]") && m.Contains(partialMessage));
        }
    }

    #endregion

    #region Test Helpers

    private static string CreateOllamaSuccessResponse(string responseText, bool done = true)
    {
        var response = new
        {
            response = responseText,
            done = done,
            total_duration = 1000000000L, // 1 second in nanoseconds
            eval_duration = 500000000L,   // 0.5 seconds in nanoseconds
            prompt_eval_count = 28,
            eval_count = 17
        };
        return JsonSerializer.Serialize(response);
    }

    private static string CreateOllamaErrorResponse(string error)
    {
        var response = new { error = error };
        return JsonSerializer.Serialize(response);
    }

    #endregion

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Generated text from Ollama"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri(EndpointOllamaDefault) };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Generated text from Ollama", result.Content);
        Assert.Equal(ModelLlama2, result.Model);
        Assert.NotNull(result.Metadata);
        Assert.Equal(ProviderOllama, result.Metadata["provider"]);
        Assert.True((bool)result.Metadata["done"]);
    }

    /// <summary>
    /// The buffered /api/generate parse hard-coded <c>TokensUsed = 0</c> behind a comment
    /// claiming "Ollama doesn't provide token count in this format" — while the live server
    /// returns <c>prompt_eval_count</c> and <c>eval_count</c> right beside the durations the
    /// same parse was already reading (verified against a real server, 2026-08-30, where the
    /// M1 probe archived <c>tokens=0</c> for a priced exchange). Zero here is not a cosmetic
    /// blank: it feeds the token dimension of <c>AgentExecutionBudget</c> and the crew
    /// accounting, so every buffered Ollama completion ran as if it were free.
    /// </summary>
    [Fact]
    public async Task ShouldAccountTokens_WhenGenerateAsyncParsesTheBufferedResponse()
    {
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("counted"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri(EndpointOllamaDefault) };
        using var provider = new OllamaLlmProvider(config, httpClientFactory, new TestLogger());

        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(28, result.PromptTokens);
        Assert.Equal(17, result.CompletionTokens);
        Assert.Equal(45, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenGenerateAsyncWithNullPrompt()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.GenerateAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("prompt", exception.ParamName);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenGenerateAsyncWithEmptyPrompt()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GenerateAsync("   ", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("prompt", exception.ParamName);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.InternalServerError,
            CreateOllamaErrorResponse("Model not found"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create("unknown-model") with { MaxRetries = 0 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithHttpRequestException()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupException(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Equal(ModelLlama2, result.Model);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Connection refused", result.Metadata["error"]);
        Assert.Equal("HttpRequestException", result.Metadata["error_type"]);
        Assert.True(logger.HasLoggedError("HTTP request failed"));
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithTimeout()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupException(new TaskCanceledException("Request timeout"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, TimeoutSeconds = 1 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Request timeout", result.Metadata["error"]);
        Assert.True(logger.HasLoggedError("Request timeout"));
    }

    [Fact]
    public async Task ShouldReturnEmptyResponse_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, "{ invalid json }");

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Equal("JSON deserialization failed", result.Metadata["error"]);
        Assert.True(logger.HasLoggedError("Failed to deserialize"));
    }

    [Fact]
    public async Task ShouldUseProvidedConfig_WhenGenerateAsyncWithCustomConfig()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Custom response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var defaultConfig = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(defaultConfig, httpClientFactory);

        var customConfig = LlmConfig.Create("codellama") with { MaxRetries = 0, Temperature = 0.9, MaxTokens = 500 };

        // Act
        var result = await provider.GenerateAsync(TestPrompt, customConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom response", result.Content);
        Assert.Equal("codellama", result.Model);

        // Verify request payload
        Assert.Single(handler.Requests);
        var requestContent = handler.RequestContents[0];
        Assert.Contains("\"model\":\"codellama\"", requestContent);
        Assert.Contains("\"temperature\":0.9", requestContent);
        Assert.Contains("\"num_predict\":500", requestContent);
    }

    [Fact]
    public async Task ShouldConstructCorrectRequestPayload_WhenGenerateAsync()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, Temperature = 0.7, MaxTokens = 1000 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:11434/api/generate", request.RequestUri?.ToString());

        var requestContent = handler.RequestContents[0];
        var payload = JsonDocument.Parse(requestContent);

        Assert.Equal(ModelLlama2, payload.RootElement.GetProperty("model").GetString());
        Assert.Equal(TestPrompt, payload.RootElement.GetProperty("prompt").GetString());
        Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());

        var options = payload.RootElement.GetProperty("options");
        Assert.Equal(0.7, options.GetProperty("temperature").GetDouble());
        Assert.Equal(1000, options.GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithNullResponse()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, "null");

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Failed to parse response", result.Metadata["error"]);
        Assert.True(logger.HasLoggedWarning("Failed to parse Ollama response"));
    }

    [Fact]
    public void ShouldReturnOllama_WhenName()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act & Assert
        Assert.Equal(ProviderOllama, provider.Name);
    }

    [Fact]
    public async Task ShouldUseProvidedUrl_WhenGenerateAsyncWithCustomBaseUrl()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri("http://custom-ollama:8080/") };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.Requests);
        Assert.Equal("http://custom-ollama:8080/api/generate",
            handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldConvertMessagesToPrompt_WhenChatAsync()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Chat response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        var messages = new[]
        {
            new LlmMessage { Role = "user", Content = "Hello" },
            new LlmMessage { Role = "assistant", Content = "Hi there!" },
            new LlmMessage { Role = "user", Content = "How are you?" }
        };

        // Act
        var result = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Chat response", result.Content);

        // Verify the prompt contains all messages
        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        Assert.Contains("Hello", requestContent);
        Assert.Contains("Hi there!", requestContent);
        Assert.Contains("How are you?", requestContent);
    }

    [Fact]
    public async Task ShouldRemoveAuthorizationHeader_WhenConfigureHttpClient()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Test response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, ApiKey = "should-be-ignored" };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        // Make a request to trigger CreateHttpClient which calls ConfigureHttpClient
        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        // The request should have been made without authorization header
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task ShouldAccumulateContent_WhenGenerateAsyncWithStreamingResponse()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        var response = new
        {
            response = "Part of the response",
            done = false,
            total_duration = 500000000L
        };
        handler.SetupResponse(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Part of the response", result.Content);
        Assert.False((bool)result.Metadata["done"]);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenGenerateAsyncWithVeryLongPrompt()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response to long prompt"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        var longPrompt = string.Join(" ", Enumerable.Repeat("This is a very long prompt.", 1000));

        // Act
        var result = await provider.GenerateAsync(longPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response to long prompt", result.Content);
        Assert.Equal(ModelLlama2, result.Model);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenChatAsyncWithEmptyMessages()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        var emptyMessages = Array.Empty<LlmMessage>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => provider.ChatAsync(emptyMessages, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenChatAsyncWithNullMessages()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act & Assert
        // ArgumentNullException (a subtype of ArgumentException) since LLM-07: the ChatAsync
        // override guards the message array before deciding which endpoint to target.
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.ChatAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldIncludeInPrompt_WhenGenerateAsyncWithSystemMessage()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with
        {
            CustomParameters = new Dictionary<string, object> { ["system_message"] = "You are a helpful assistant." }
        };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        Assert.Contains("You are a helpful assistant", requestContent);
        Assert.Contains(TestPrompt, requestContent);
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenGenerateAsyncWithCancellationToken()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupDelay(5000); // 5 second delay
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Cancel after 100ms

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Theory]
    [InlineData(ModelGpt35Turbo)]
    [InlineData("claude-2")]
    [InlineData("palm-2")]
    public async Task ShouldUseCorrectModel_WhenGenerateAsyncWithDifferentModels(string modelName)
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse($"Response from {modelName}"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(modelName) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"Response from {modelName}", result.Content);
        Assert.Equal(modelName, result.Model);

        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        Assert.Contains($"\"model\":\"{modelName}\"", requestContent);
    }

    [Fact]
    public async Task ShouldOmitNumPredict_WhenGenerateAsyncWithMaxTokensZero()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, MaxTokens = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Act
        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        var payload = JsonDocument.Parse(requestContent);
        var options = payload.RootElement.GetProperty("options");

        // num_predict should not be present when MaxTokens is 0
        Assert.False(options.TryGetProperty("num_predict", out _));
    }

    [Fact]
    public async Task ShouldUseDefaultConfig_WhenGenerateAsyncWithNullConfig()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Default response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var defaultConfig = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, Temperature = 0.5, MaxTokens = 100 };
        using var provider = new OllamaLlmProvider(defaultConfig, httpClientFactory);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, config: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Default response", result.Content);
        Assert.Equal(ModelLlama2, result.Model);

        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        Assert.Contains("\"temperature\":0.5", requestContent);
        Assert.Contains("\"num_predict\":100", requestContent);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithBadGateway()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.BadGateway, "Bad Gateway");

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
        Assert.True(logger.HasLoggedError("HTTP"));
    }

    [Fact]
    public async Task ShouldReturnErrorWithRetryInfo_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse((HttpStatusCode)429, CreateOllamaErrorResponse("Rate limit exceeded"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        var logger = new TestLogger();
        using var provider = new OllamaLlmProvider(config, httpClientFactory, logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
        Assert.Equal("Rate limit exceeded", result.Metadata["error"]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullHttpClientFactory()
    {
        // Arrange
        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OllamaLlmProvider(config, null!));

    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullConfig()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OllamaLlmProvider(null!, httpClientFactory));

    }

    [Fact]
    public async Task ShouldIncludeInConversion_WhenChatAsyncWithSystemMessage()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Chat response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        var messages = new[]
        {
            new LlmMessage { Role = "system", Content = "You are helpful." },
            new LlmMessage { Role = "user", Content = "Hello" }
        };

        // Act
        var result = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        Assert.Contains("You are helpful", requestContent);
        Assert.Contains("Hello", requestContent);
    }

    #region BaseUrl Resolution Tests

    [Fact]
    public void Constructor_ShouldUseConfigBaseUrl_WhenProvided()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri("http://ollama.example.com:11434") };

        // Act
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Assert
        Assert.Equal(new Uri("http://ollama.example.com:11434"), provider.BaseUrl);
    }

    [Fact]
    public void Constructor_ShouldTrimTrailingSlash_WhenConfigBaseUrlHasTrailingSlash()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri("http://ollama.example.com:11434/") };

        // Act
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Assert
        Assert.Equal(new Uri("http://ollama.example.com:11434"), provider.BaseUrl);
    }

    [Fact]
    public void Constructor_ShouldUseEnvironmentVariable_WhenConfigBaseUrlNotProvided()
    {
        // Arrange
        const string customUrl = "http://ollama.docker.local:11434";
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", customUrl);

        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2); // BaseUrl = null

        try
        {
            // Act
            using var provider = new OllamaLlmProvider(config, httpClientFactory);

            // Assert
            Assert.Equal(new Uri(customUrl), provider.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", null);
        }
    }

    [Fact]
    public void Constructor_ShouldTrimTrailingSlash_WhenEnvironmentVariableHasTrailingSlash()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", "http://ollama.docker.local:11434/");

        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2); // BaseUrl = null

        try
        {
            // Act
            using var provider = new OllamaLlmProvider(config, httpClientFactory);

            // Assert
            Assert.Equal(new Uri("http://ollama.docker.local:11434"), provider.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", null);
        }
    }

    [Fact]
    public void Constructor_ShouldUseDefaultBaseUrl_WhenNothingProvided()
    {
        // Arrange — ensure env var is not set
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", null);

        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2); // BaseUrl = null

        // Act
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        // Assert
        Assert.Equal(new Uri(EndpointOllamaDefault), provider.BaseUrl);
    }

    [Fact]
    public void Constructor_ShouldPreferConfigBaseUrl_OverEnvironmentVariable()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", "http://env-var-url:11434");

        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri("http://config-url:11434") };

        try
        {
            // Act
            using var provider = new OllamaLlmProvider(config, httpClientFactory);

            // Assert — config.BaseUrl takes priority over env var
            Assert.Equal(new Uri("http://config-url:11434"), provider.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", null);
        }
    }

    [Fact]
    public void Constructor_WithResiliencePolicy_ShouldUseConfigBaseUrl_WhenProvided()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0, BaseUrl = new Uri("http://ollama.k8s.local:11434") };

        // Act — use the constructor overload with resilience policy
        using var provider = new OllamaLlmProvider(config, httpClientFactory, resiliencePolicy: null);

        // Assert
        Assert.Equal(new Uri("http://ollama.k8s.local:11434"), provider.BaseUrl);
    }

    [Fact]
    public void Constructor_WithResiliencePolicy_ShouldUseEnvironmentVariable_WhenConfigBaseUrlNotProvided()
    {
        // Arrange
        const string customUrl = "http://ollama.ci:11434";
        Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", customUrl);

        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient());
        var config = LlmConfig.Create(ModelLlama2); // BaseUrl = null

        try
        {
            // Act — use the constructor overload with resilience policy
            using var provider = new OllamaLlmProvider(config, httpClientFactory, resiliencePolicy: null);

            // Assert
            Assert.Equal(new Uri(customUrl), provider.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OLLAMA_BASE_URL", null);
        }
    }

    #endregion

    [Fact]
    public async Task ShouldEscapeProperly_WhenGenerateAsyncWithSpecialCharactersInPrompt()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler();
        handler.SetupResponse(HttpStatusCode.OK, CreateOllamaSuccessResponse("Response"));

        var httpClient = new HttpClient(handler);
        var httpClientFactory = new TestHttpClientFactory();
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = LlmConfig.Create(ModelLlama2) with { MaxRetries = 0 };
        using var provider = new OllamaLlmProvider(config, httpClientFactory);

        var promptWithSpecialChars = "Test with \"quotes\" and \nnewlines and \\backslashes";

        // Act
        var result = await provider.GenerateAsync(promptWithSpecialChars, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response", result.Content);

        Assert.Single(handler.RequestContents);
        var requestContent = handler.RequestContents[0];
        // JSON should properly escape the special characters
        Assert.Contains("\\\"quotes\\\"", requestContent);
        Assert.Contains("\\n", requestContent);
        Assert.Contains("\\\\", requestContent);
    }
}

#pragma warning restore CS0618
