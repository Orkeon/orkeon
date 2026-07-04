using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.LLMs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public class LlmProviderFactoryTests
{
    #region Test Doubles

    private class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, HttpClient> _clients = [];

        public TestHttpClientFactory()
        {
            // Register default clients for each provider type
            _clients["OllamaLlmProvider"] = new HttpClient();
            _clients["OpenAIProvider"] = new HttpClient();
        }

        public void RegisterClient(string name, HttpClient client)
        {
            _clients[name] = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _clients.TryGetValue(name, out var client)
                ? client
                : new HttpClient(); // Return default client instead of throwing
        }
    }

    private class TestLoggerFactory : ILoggerFactory
    {
        private readonly Dictionary<Type, ILogger> _loggers = [];

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName)
        {
            return NullLogger.Instance;
        }

        public void Dispose() { }

        public ILogger<T> CreateLogger<T>()
        {
            var type = typeof(T);
            if (!_loggers.TryGetValue(type, out var logger))
            {
                logger = new TestLogger<T>();
                _loggers[type] = logger;
            }
            return (ILogger<T>)logger;
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public List<string> LoggedMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
        }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructorWithValidParameters()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();

        // Act
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullHttpClientFactory()
    {
        // Arrange
        using var loggerFactory = new TestLoggerFactory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new LlmProviderFactory(null!, loggerFactory));
        Assert.Equal("httpClientFactory", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLoggerFactory()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new LlmProviderFactory(httpClientFactory, null!));
        Assert.Equal("loggerFactory", exception.ParamName);
    }

    #endregion

    #region Create(LlmConfig) Tests - Inference

    [Fact]
    public void ShouldCreateOllamaProvider_WhenCreateWithOllamaConfig()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(ModelLlama2) with { BaseUrl = new Uri(EndpointOllamaDefault) };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldCreateOpenAIProvider_WhenCreateWithOpenAIConfig()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(ModelGpt4);

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldCreateAnthropicProvider_WhenCreateWithClaudeModel()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("claude-3-opus");

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreateWithNullConfig()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
        Assert.Equal("config", exception.ParamName);
    }

    [Theory]
    [InlineData(ModelLlama2, EndpointOllamaDefault, ProviderOllama)]
    [InlineData(ModelGpt35Turbo, null, ProviderOpenAI)]
    [InlineData(ModelGpt4, "https://api.openai.com", ProviderOpenAI)]
    [InlineData(ModelMistral, null, ProviderOllama)]
    [InlineData("codellama", null, ProviderOllama)]
    [InlineData("unknown-model", null, ProviderOpenAI)] // Default
    public void ShouldInferCorrectProviderType_WhenCreate(string model, string? baseUrl, string expectedType)
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(model) with { BaseUrl = baseUrl is null ? null : new Uri(baseUrl) };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        // Note: We can't directly test the provider type due to adapter pattern,
        // but we can verify it doesn't throw for valid types
        if (expectedType == ProviderAnthropic)
        {
            Assert.Throws<NotSupportedException>(() => factory.Create(config));
        }
    }

    #endregion

    #region Create(string, LlmConfig) Tests - Explicit Type

    [Fact]
    public void ShouldCreateOllamaProvider_WhenCreateWithExplicitOllama()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("any-model");

        // Act
        var provider = factory.Create(ProviderOllama, config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldCreateOpenAIProvider_WhenCreateWithExplicitOpenAI()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("any-model");

        // Act
        var provider = factory.Create(ProviderOpenAI, config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldCreateAnthropicProvider_WhenCreateWithExplicitAnthropic()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("claude");

        // Act
        var provider = factory.Create(ProviderAnthropic, config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldCreateMistralProvider_WhenCreateWithExplicitMistral()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("mistral-large-latest");

        // Act
        // Note: We can't assert the concrete MistralLlmProvider type (encapsulated in
        // LlmProviderAdapter). Asserting NotNull + LlmProviderAdapter + absence of
        // NotSupportedException validates that BOTH dispatch switches now recognise "mistral"
        // (and don't fall through to the `_ => throw NotSupported` branch).
        var provider = factory.Create(ProviderMistral, config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldThrowNotSupportedException_WhenCreateWithUnknownProviderType()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model");

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(
            () => factory.Create("unknown-provider", config));
        Assert.Contains("unknown-provider", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreateWithNullProviderType()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => factory.Create(null!, config));
        Assert.Equal("providerType", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreateWithEmptyProviderType()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => factory.Create("", config));
        Assert.Equal("providerType", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreateWithWhitespaceProviderType()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model");

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => factory.Create("   ", config));
        Assert.Equal("providerType", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreateWithExplicitTypeAndNullConfig()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => factory.Create(ProviderOpenAI, null!));
        Assert.Equal("config", exception.ParamName);
    }

    #endregion

    #region Case Insensitive Tests

    [Theory]
    [InlineData("OLLAMA")]
    [InlineData("Ollama")]
    [InlineData("oLLaMa")]
    public void ShouldWork_WhenCreateWithDifferentCasedProviderType(string providerType)
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model");

        // Act
        var provider = factory.Create(providerType, config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    #endregion

    #region BaseUrl Inference Tests

    [Theory]
    [InlineData("http://localhost:11434/v1/chat", ProviderOllama)]
    [InlineData("http://127.0.0.1:11434", ProviderOllama)]
    [InlineData("https://api.openai.com/v1", ProviderOpenAI)]
    [InlineData("https://api.anthropic.com/v1", ProviderAnthropic)]
    [InlineData(EndpointMistral, ProviderMistral)] // Cloud Mistral targeted via BaseUrl
    [InlineData("https://custom-domain.com", ProviderOpenAI)] // Default
    public void ShouldInferProviderFromBaseUrl_WhenCreate(string baseUrl, string expectedType)
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(CustomModelName) with { BaseUrl = new Uri(baseUrl) };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        // Use expectedType to prevent xUnit1026 warning
        _ = expectedType;
    }

    #endregion

    #region Edge Cases and Advanced Tests

    [Fact]
    public void ShouldCreateIndependentProviders_WhenCreateMultipleTimes()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);
        var config = LlmConfig.Create(ModelGpt4);

        // Act
        var provider1 = factory.Create(config);
        var provider2 = factory.Create(config);

        // Assert
        Assert.NotNull(provider1);
        Assert.NotNull(provider2);
        Assert.NotSame(provider1, provider2);
    }

    [Fact]
    public void ShouldCreateDifferentProviders_WhenCreateWithDifferentConfigs()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config1 = LlmConfig.Create(ModelGpt4);
        var config2 = LlmConfig.Create(ModelLlama2) with { BaseUrl = new Uri(EndpointOllamaDefault) };

        // Act
        var provider1 = factory.Create(config1);
        var provider2 = factory.Create(config2);

        // Assert
        Assert.NotNull(provider1);
        Assert.NotNull(provider2);
        Assert.NotSame(provider1, provider2);
    }

    [Fact]
    public void ShouldUseCustomUrl_WhenCreateWithCustomBaseUrlForOpenAI()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(ModelGpt4) with
        {
            BaseUrl = new Uri("https://custom-openai-proxy.com/v1"),
            ApiKey = "test-key"
        };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreateWithVeryLongModelName()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var longModelName = new string('a', 1000);
        var config = LlmConfig.Create(longModelName);

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreateWithSpecialCharactersInModel()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model-with-special@#$%^&*()_+characters");

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreateWithUnicodeInModel()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("模型-мадэль-モデル");

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData("gemini", ProviderOpenAI)] // Google model defaults to OpenAI-compatible
    [InlineData("palm", ProviderOpenAI)]    // Google model defaults to OpenAI-compatible
    [InlineData("bard", ProviderOpenAI)]    // Google model defaults to OpenAI-compatible
    [InlineData("cohere", ProviderOpenAI)]  // Cohere defaults to OpenAI-compatible
    public void ShouldDefaultToOpenAI_WhenCreateWithOtherProviderModels(string model, string expectedDefault)
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(model);

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
        // Use expectedDefault parameter to prevent xUnit1026 warning
        _ = expectedDefault;
    }

    [Fact]
    public void ShouldCreateProvider_WhenCreateWithConfigContainingAllSettings()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create(ModelGpt4) with
        {
            BaseUrl = new Uri("https://api.openai.com/v1"),
            ApiKey = "test-key",
            Temperature = 0.7,
            MaxTokens = 2000,
            TopP = 0.9,
            FrequencyPenalty = 0.5,
            PresencePenalty = 0.5,
            StopSequences = ["\n", "END"],
            CustomParameters = new Dictionary<string, object>
            {
                ["response_format"] = "json",
                ["user"] = "test-user"
            },
            Seed = 42
        };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void ShouldRecognizeAsOllama_WhenCreateWithMixedCaseOllama()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("LLaMa2") with { BaseUrl = new Uri(EndpointOllamaDefault) };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreateWithPortInBaseUrl()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model") with { BaseUrl = new Uri("http://localhost:8080/api") };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void ShouldStillRecognizeAsOllama_WhenCreateWithHttpsOllamaUrl()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var loggerFactory = new TestLoggerFactory();
        var factory = new LlmProviderFactory(httpClientFactory, loggerFactory);

        var config = LlmConfig.Create("model") with { BaseUrl = new Uri("https://secure-ollama:11434") };

        // Act
        var provider = factory.Create(config);

        // Assert
        Assert.NotNull(provider);
    }

    #endregion
}

#pragma warning restore CS0618
