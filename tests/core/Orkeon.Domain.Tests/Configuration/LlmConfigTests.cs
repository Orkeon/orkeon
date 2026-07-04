using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for LLM Configuration following Clean Architecture principles.
/// Tests the language model configuration record and its behavior.
/// </summary>
public class LlmConfigTests
{
    private static readonly string[] CodeInterpreterRetrieval = ["code_interpreter", "retrieval"];
    private static readonly string[] s_stopSequences = ["END", "STOP", "\n\n"];
    private static readonly string[] s_codeStopSequences = ["```", "END_CODE", "\n\n\n"];

    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingLlmConfigWithDefaultConstructor()
    {
        // Act
        var config = LlmConfig.Default();

        // Assert
        Assert.Equal(ModelGpt4, config.Model);
        Assert.Null(config.ApiKey);
        Assert.Null(config.BaseUrl);
        Assert.Equal(0.7, config.Temperature, precision: 1);
        Assert.Equal(4096, config.MaxTokens);
        Assert.Equal(1.0, config.TopP, precision: 1);
        Assert.Equal(0.0, config.FrequencyPenalty, precision: 1);
        Assert.Equal(0.0, config.PresencePenalty, precision: 1);
        Assert.Null(config.Seed);
        Assert.NotNull(config.StopSequences);
        Assert.Empty(config.StopSequences);
        Assert.NotNull(config.CustomParameters);
        Assert.Empty(config.CustomParameters);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.Equal(3, config.MaxRetries);
    }

    [Fact]
    public void ShouldSetModel_WhenUsingLlmConfigUsingConstructorWithModel()
    {
        // Act
        var config = LlmConfig.Create(ModelGpt35Turbo);

        // Assert
        Assert.Equal(ModelGpt35Turbo, config.Model);
        Assert.Null(config.ApiKey);
        Assert.Equal(0.7, config.Temperature, precision: 1);
        Assert.Equal(4096, config.MaxTokens);
    }

    [Fact]
    public void ShouldSetBoth_WhenUsingLlmConfigUsingConstructorWithModelAndApiKey()
    {
        // Act
        var config = LlmConfig.Create(ModelClaude3, TestApiKey);

        // Assert
        Assert.Equal(ModelClaude3, config.Model);
        Assert.Equal(TestApiKey, config.ApiKey);
        Assert.Equal(0.7, config.Temperature, precision: 1);
        Assert.Equal(4096, config.MaxTokens);
    }

    [Fact]
    public void ShouldThrow_WhenCreatingLlmConfigWithNullModel()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => LlmConfig.Create(null!, null));
    }

    [Fact]
    public void ShouldThrow_WhenCreatingLlmConfigWithEmptyModel()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => LlmConfig.Create(""));
    }

    [Fact]
    public void ShouldThrow_WhenCreatingLlmConfigWithWhitespaceModel()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => LlmConfig.Create("   "));
    }

    #endregion

    #region Property Tests

    [Fact]
    public void ShouldBeSettable_WhenUsingLlmConfigUsingProperties()
    {
        // Arrange
        var stopSequences = new List<string> { "END", "STOP", "\n\n" };
        var customParameters = new Dictionary<string, object>
        {
            { "customParam1", "value1" },
            { "customParam2", 42 },
            { "customParam3", true }
        };

        // Act
        var config = LlmConfig.Default() with
        {
            Model = "llama-2-70b",
            ApiKey = CustomApiKey,
            BaseUrl = new Uri("https://api.custom.com"),
            Temperature = 0.9,
            MaxTokens = 8192,
            TopP = 0.95,
            FrequencyPenalty = 0.5,
            PresencePenalty = 0.3,
            Seed = 12345,
            StopSequences = stopSequences,
            CustomParameters = customParameters,
            TimeoutSeconds = 60,
            MaxRetries = 5
        };

        // Assert
        Assert.Equal("llama-2-70b", config.Model);
        Assert.Equal(CustomApiKey, config.ApiKey);
        Assert.Equal(new Uri("https://api.custom.com"), config.BaseUrl);
        Assert.Equal(0.9, config.Temperature, precision: 1);
        Assert.Equal(8192, config.MaxTokens);
        Assert.Equal(0.95, config.TopP, precision: 2);
        Assert.Equal(0.5, config.FrequencyPenalty, precision: 1);
        Assert.Equal(0.3, config.PresencePenalty, precision: 1);
        Assert.Equal(12345, config.Seed);
        Assert.Equal(stopSequences, config.StopSequences);
        Assert.Equal(3, config.StopSequences.Count);
        Assert.Equal(customParameters, config.CustomParameters);
        Assert.Equal(3, config.CustomParameters.Count);
        Assert.Equal(60, config.TimeoutSeconds);
        Assert.Equal(5, config.MaxRetries);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ShouldAcceptVariousValues_WhenUsingLlmConfigUsingTemperature(double temperature)
    {
        // Act
        var config = LlmConfig.Default() with { Temperature = temperature };

        // Assert
        Assert.Equal(temperature, config.Temperature, precision: 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(32000)]
    public void ShouldAcceptVariousValues_WhenUsingLlmConfigWithMaxTokens(int maxTokens)
    {
        // Act
        var config = LlmConfig.Default() with { MaxTokens = maxTokens };

        // Assert
        Assert.Equal(maxTokens, config.MaxTokens);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigUsingTopP(double topP)
    {
        // Act
        var config = LlmConfig.Default() with { TopP = topP };

        // Assert
        Assert.Equal(topP, config.TopP, precision: 1);
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void ShouldAcceptVariousValues_WhenUsingLlmConfigUsingFrequencyPenalty(double penalty)
    {
        // Act
        var config = LlmConfig.Default() with { FrequencyPenalty = penalty };

        // Assert
        Assert.Equal(penalty, config.FrequencyPenalty, precision: 1);
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void ShouldAcceptVariousValues_WhenUsingLlmConfigUsingPresencePenalty(double penalty)
    {
        // Act
        var config = LlmConfig.Default() with { PresencePenalty = penalty };

        // Assert
        Assert.Equal(penalty, config.PresencePenalty, precision: 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(12345)]
    [InlineData(-1)]
    [InlineData(0)]
    public void ShouldAcceptNullableValues_WhenUsingLlmConfigSeeding(int? seed)
    {
        // Act
        var config = LlmConfig.Default() with { Seed = seed };

        // Assert
        Assert.Equal(seed, config.Seed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(300)]
    public void ShouldAcceptVariousValues_WhenUsingLlmConfigUsingTimeoutSeconds(int timeoutSeconds)
    {
        // Act
        var config = LlmConfig.Default() with { TimeoutSeconds = timeoutSeconds };

        // Assert
        Assert.Equal(timeoutSeconds, config.TimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void ShouldAcceptVariousValues_WhenUsingLlmConfigWithMaxRetries(int maxRetries)
    {
        // Act
        var config = LlmConfig.Default() with { MaxRetries = maxRetries };

        // Assert
        Assert.Equal(maxRetries, config.MaxRetries);
    }

    #endregion

    #region Collection Properties Tests

    [Fact]
    public void ShouldSupportInitialization_WhenUsingLlmConfigStoppingSequences()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            StopSequences = ["END", "STOP", "\n\n", "```"]
        };

        // Assert
        Assert.Equal(4, config.StopSequences.Count);
        Assert.Contains("END", config.StopSequences);
        Assert.Contains("STOP", config.StopSequences);
        Assert.Contains("\n\n", config.StopSequences);
        Assert.Contains("```", config.StopSequences);
    }

    [Fact]
    public void ShouldSupportInitialization_WhenUsingLlmConfigWithCustomParameters()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            CustomParameters = new Dictionary<string, object>
            {
                { "logprobs", true },
                { "top_logprobs", 5 },
                { "response_format", new { type = "json_object" } },
                { "tools", CodeInterpreterRetrieval }
            }
        };

        // Assert
        Assert.Equal(4, config.CustomParameters.Count);
        Assert.True((bool)config.CustomParameters["logprobs"]);
        Assert.Equal(5, config.CustomParameters["top_logprobs"]);
        Assert.NotNull(config.CustomParameters["response_format"]);
        Assert.IsType<string[]>(config.CustomParameters["tools"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingLlmConfigWithCustomParametersWithComplexObjects()
    {
        // Arrange
        var complexParameter = new
        {
            Type = "function",
            Function = new
            {
                Name = "get_weather",
                Description = "Get current weather",
                Parameters = new
                {
                    Type = "object",
                    Properties = new { Location = new { Type = "string" } }
                }
            }
        };

        // Act
        var config = LlmConfig.Default() with
        {
            CustomParameters = new Dictionary<string, object>
            {
                { "function_call", complexParameter },
                { "stream", false },
                { "user", "user-123" }
            }
        };

        // Assert
        Assert.Equal(3, config.CustomParameters.Count);
        Assert.Equal(complexParameter, config.CustomParameters["function_call"]);
        Assert.False((bool)config.CustomParameters["stream"]);
        Assert.Equal("user-123", config.CustomParameters["user"]);
    }

    #endregion

    #region Static Factory Methods Tests

    [Fact]
    public void ShouldReturnDefaultConfiguration_WhenUsingLlmConfigWithDefault()
    {
        // Act
        var config = LlmConfig.Default();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelGpt4, config.Model);
        Assert.Null(config.ApiKey);
        Assert.Equal(0.7, config.Temperature, precision: 1);
        Assert.Equal(4096, config.MaxTokens);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.Equal(3, config.MaxRetries);
    }

    [Fact]
    public void ShouldReturnGpt4Configuration_WhenUsingLlmConfigUsingGpt4()
    {
        // Act
        var config = LlmConfig.Gpt4();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelGpt4, config.Model);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void ShouldSetApiKey_WhenUsingLlmConfigUsingGpt4WithApiKey()
    {
        // Act
        var config = LlmConfig.Gpt4("test-gpt4-key");

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelGpt4, config.Model);
        Assert.Equal("test-gpt4-key", config.ApiKey);
    }

    [Fact]
    public void ShouldReturnGpt35TurboConfiguration_WhenUsingLlmConfigUsingGpt35Turbo()
    {
        // Act
        var config = LlmConfig.Gpt35Turbo();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelGpt35Turbo, config.Model);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void ShouldSetApiKey_WhenUsingLlmConfigUsingGpt35TurboWithApiKey()
    {
        // Act
        var config = LlmConfig.Gpt35Turbo("test-gpt35-key");

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelGpt35Turbo, config.Model);
        Assert.Equal("test-gpt35-key", config.ApiKey);
    }

    [Fact]
    public void ShouldReturnClaudeConfiguration_WhenUsingLlmConfigUsingClaude()
    {
        // Act
        var config = LlmConfig.Claude();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelClaude3Opus, config.Model);
        Assert.Equal(new Uri("https://api.anthropic.com"), config.BaseUrl);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void ShouldSetApiKey_WhenUsingLlmConfigUsingClaudeWithApiKey()
    {
        // Act
        var config = LlmConfig.Claude("test-claude-key");

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelClaude3Opus, config.Model);
        Assert.Equal(new Uri("https://api.anthropic.com"), config.BaseUrl);
        Assert.Equal("test-claude-key", config.ApiKey);
    }

    [Fact]
    public void ShouldReturnOllamaConfiguration_WhenUsingLlmConfigUsingOllama()
    {
        // Act
        var config = LlmConfig.Ollama();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(ModelLlama2, config.Model);
        Assert.Equal(new Uri(EndpointOllamaDefault), config.BaseUrl);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void ShouldSetModel_WhenUsingLlmConfigUsingOllamaWithCustomModel()
    {
        // Act
        var config = LlmConfig.Ollama("codellama");

        // Assert
        Assert.NotNull(config);
        Assert.Equal("codellama", config.Model);
        Assert.Equal(new Uri(EndpointOllamaDefault), config.BaseUrl);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void ShouldReturnNewInstances_WhenUsingLlmConfigUsingStaticFactories()
    {
        // Act
        var default1 = LlmConfig.Default();
        var default2 = LlmConfig.Default();
        var gpt4_1 = LlmConfig.Gpt4();
        var gpt4_2 = LlmConfig.Gpt4();

        // Assert
        Assert.NotSame(default1, default2);
        Assert.NotSame(gpt4_1, gpt4_2);
    }

    #endregion

    #region Provider-Specific Configuration Tests

    [Fact]
    public void ShouldHaveCorrectModels_WhenUsingLlmConfigOpeningAIProviders()
    {
        // Act
        var gpt4 = LlmConfig.Gpt4("key");
        var gpt35 = LlmConfig.Gpt35Turbo("key");

        // Assert
        Assert.Equal(ModelGpt4, gpt4.Model);
        Assert.Equal(ModelGpt35Turbo, gpt35.Model);
        Assert.Equal("key", gpt4.ApiKey);
        Assert.Equal("key", gpt35.ApiKey);
        Assert.Null(gpt4.BaseUrl);
        Assert.Null(gpt35.BaseUrl);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenUsingLlmConfigUsingAnthropicProvider()
    {
        // Act
        var claude = LlmConfig.Claude("anthropic-key");

        // Assert
        Assert.Equal(ModelClaude3Opus, claude.Model);
        Assert.Equal("anthropic-key", claude.ApiKey);
        Assert.Equal(new Uri("https://api.anthropic.com"), claude.BaseUrl);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenUsingLlmConfigUsingOllamaProvider()
    {
        // Act
        var ollama = LlmConfig.Ollama(ModelMistral);

        // Assert
        Assert.Equal(ModelMistral, ollama.Model);
        Assert.Null(ollama.ApiKey);
        Assert.Equal(new Uri(EndpointOllamaDefault), ollama.BaseUrl);
    }

    [Fact]
    public void ShouldHaveDistinctConfigurations_WhenUsingLlmConfigWithDifferentProviders()
    {
        // Act
        var openai = LlmConfig.Gpt4("openai-key");
        var anthropic = LlmConfig.Claude("anthropic-key");
        var ollama = LlmConfig.Ollama(ModelLlama2);

        // Assert
        Assert.NotEqual(openai.Model, anthropic.Model);
        Assert.NotEqual(openai.Model, ollama.Model);
        Assert.NotEqual(anthropic.Model, ollama.Model);

        Assert.NotEqual(openai.BaseUrl, anthropic.BaseUrl);
        Assert.NotEqual(openai.BaseUrl, ollama.BaseUrl);
        Assert.NotEqual(anthropic.BaseUrl, ollama.BaseUrl);

        Assert.NotNull(openai.ApiKey);
        Assert.NotNull(anthropic.ApiKey);
        Assert.Null(ollama.ApiKey);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldMaintainAllSettings_WhenUsingLlmConfigWithFullConfiguration()
    {
        // Arrange & Act
        var config = LlmConfig.Create(CustomModelName, "custom-key") with
        {
            BaseUrl = new Uri("https://api.custom.com"),
            Temperature = 0.8,
            MaxTokens = 2048,
            TopP = 0.9,
            FrequencyPenalty = 0.2,
            PresencePenalty = 0.1,
            Seed = 42,
            TimeoutSeconds = 45,
            MaxRetries = 4,
            StopSequences = [.. s_stopSequences],
            CustomParameters = new Dictionary<string, object>
            {
                { "stream", true },
                { "logprobs", 5 }
            }
        };

        // Assert
        Assert.Equal(CustomModelName, config.Model);
        Assert.Equal("custom-key", config.ApiKey);
        Assert.Equal(new Uri("https://api.custom.com"), config.BaseUrl);
        Assert.Equal(0.8, config.Temperature, precision: 1);
        Assert.Equal(2048, config.MaxTokens);
        Assert.Equal(0.9, config.TopP, precision: 1);
        Assert.Equal(0.2, config.FrequencyPenalty, precision: 1);
        Assert.Equal(0.1, config.PresencePenalty, precision: 1);
        Assert.Equal(42, config.Seed);
        Assert.Equal(45, config.TimeoutSeconds);
        Assert.Equal(4, config.MaxRetries);
        Assert.Equal(3, config.StopSequences.Count);
        Assert.Equal(2, config.CustomParameters.Count);
    }

    [Fact]
    public void ShouldConfigureForConversation_WhenUsingLlmConfigUsingChatCompletionSettings()
    {
        // Act
        var config = LlmConfig.Gpt4("chat-key") with
        {
            Temperature = 0.7,
            MaxTokens = 1500,
            TopP = 1.0,
            FrequencyPenalty = 0.0,
            PresencePenalty = 0.6,
            StopSequences = ["Human:", "Assistant:"]
        };

        // Assert
        Assert.Equal(ModelGpt4, config.Model);
        Assert.Equal(0.7, config.Temperature, precision: 1);
        Assert.Equal(1500, config.MaxTokens);
        Assert.Equal(0.6, config.PresencePenalty, precision: 1);
        Assert.Contains("Human:", config.StopSequences);
        Assert.Contains("Assistant:", config.StopSequences);
    }

    [Fact]
    public void ShouldConfigureForCoding_WhenUsingLlmConfigUsingCodeGenerationSettings()
    {
        // Act
        var config = LlmConfig.Gpt4("code-key") with
        {
            Temperature = 0.2,
            MaxTokens = 4096,
            TopP = 0.95,
            StopSequences = [.. s_codeStopSequences],
            CustomParameters = new Dictionary<string, object>
            {
                { "response_format", new { type = "json_object" } }
            }
        };

        // Assert
        Assert.Equal(0.2, config.Temperature, precision: 1);
        Assert.Equal(4096, config.MaxTokens);
        Assert.Contains("```", config.StopSequences);
        Assert.Contains("END_CODE", config.StopSequences);
        Assert.Contains("response_format", config.CustomParameters.Keys);
    }

    [Fact]
    public void ShouldConfigureForOllama_WhenUsingLlmConfigUsingLocalModelSettings()
    {
        // Act
        var config = LlmConfig.Ollama("codellama:13b") with
        {
            Temperature = 0.1,
            MaxTokens = 8192,
            TimeoutSeconds = 120,
            MaxRetries = 2
        };

        // Assert
        Assert.Equal("codellama:13b", config.Model);
        Assert.Equal(new Uri(EndpointOllamaDefault), config.BaseUrl);
        Assert.Null(config.ApiKey);
        Assert.Equal(0.1, config.Temperature, precision: 1);
        Assert.Equal(8192, config.MaxTokens);
        Assert.Equal(120, config.TimeoutSeconds);
        Assert.Equal(2, config.MaxRetries);
    }

    [Fact]
    public void ShouldConfigureForCreativeWork_WhenUsingLlmConfigUsingHighCreativitySettings()
    {
        // Act
        var config = LlmConfig.Gpt4("creative-key") with
        {
            Temperature = 1.0,
            TopP = 0.9,
            FrequencyPenalty = 0.5,
            PresencePenalty = 0.5,
            MaxTokens = 2048
        };

        // Assert
        Assert.Equal(1.0, config.Temperature, precision: 1);
        Assert.Equal(0.9, config.TopP, precision: 1);
        Assert.Equal(0.5, config.FrequencyPenalty, precision: 1);
        Assert.Equal(0.5, config.PresencePenalty, precision: 1);
        Assert.Equal(2048, config.MaxTokens);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldAcceptValues_WhenUsingLlmConfigWithExtremeValues()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            Temperature = -1.0,
            MaxTokens = -1,
            TopP = -1.0,
            FrequencyPenalty = -3.0,
            PresencePenalty = 3.0,
            TimeoutSeconds = 0,
            MaxRetries = -1
        };

        // Assert - No validation constraints in the record itself
        Assert.Equal(-1.0, config.Temperature, precision: 1);
        Assert.Equal(-1, config.MaxTokens);
        Assert.Equal(-1.0, config.TopP, precision: 1);
        Assert.Equal(-3.0, config.FrequencyPenalty, precision: 1);
        Assert.Equal(3.0, config.PresencePenalty, precision: 1);
        Assert.Equal(0, config.TimeoutSeconds);
        Assert.Equal(-1, config.MaxRetries);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingLlmConfigWithNullCollections()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            StopSequences = null!,
            CustomParameters = null!
        };

        // Assert
        Assert.Null(config.StopSequences);
        Assert.Null(config.CustomParameters);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingLlmConfigWithVeryLargeValues()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            MaxTokens = int.MaxValue,
            TimeoutSeconds = int.MaxValue,
            MaxRetries = int.MaxValue,
            Seed = int.MaxValue
        };

        // Assert
        Assert.Equal(int.MaxValue, config.MaxTokens);
        Assert.Equal(int.MaxValue, config.TimeoutSeconds);
        Assert.Equal(int.MaxValue, config.MaxRetries);
        Assert.Equal(int.MaxValue, config.Seed);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingLlmConfigWithUnicodeContent()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            Model = "模型-gpt-4-中文",
            ApiKey = "密钥-αβγ-key",
            BaseUrl = new Uri("https://api.测试.com"),
            StopSequences = ["结束", "停止"]
        };

        // Assert
        Assert.Contains("模型", config.Model);
        Assert.Contains("中文", config.Model);
        Assert.Contains("密钥", config.ApiKey!);
        Assert.Contains("αβγ", config.ApiKey!);
        Assert.Contains("测试", config.BaseUrl!.ToString());
        Assert.Contains("结束", config.StopSequences);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingLlmConfigToString()
    {
        // Arrange
        var config = LlmConfig.Create(TestModelName);

        // Act
        var stringRepresentation = config.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("LlmConfig", stringRepresentation);
    }

    [Fact]
    public void ShouldAcceptEmptyValues_WhenUsingLlmConfigWithEmptyStrings()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            Model = "",
            ApiKey = "",
            BaseUrl = null
        };

        // Assert
        Assert.Equal("", config.Model);
        Assert.Equal("", config.ApiKey);
        Assert.Null(config.BaseUrl);
    }

    [Fact]
    public void ShouldAllowDuplicates_WhenUsingLlmConfigStoppingSequencesWithDuplicates()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            StopSequences = ["STOP", "STOP", "END", "STOP"]
        };

        // Assert
        Assert.Equal(4, config.StopSequences.Count);
        Assert.Equal(3, config.StopSequences.Count(s => s == "STOP"));
        Assert.Equal(1, config.StopSequences.Count(s => s == "END"));
    }

    [Fact]
    public void ShouldAcceptNulls_WhenUsingLlmConfigWithCustomParametersWithNullValues()
    {
        // Act
        var config = LlmConfig.Default() with
        {
            CustomParameters = new Dictionary<string, object>
            {
                { "nullParam", null! },
                { "stringParam", "value" },
                { "boolParam", false }
            }
        };

        // Assert
        Assert.Equal(3, config.CustomParameters.Count);
        Assert.Null(config.CustomParameters["nullParam"]);
        Assert.Equal("value", config.CustomParameters["stringParam"]);
        Assert.False((bool)config.CustomParameters["boolParam"]);
    }

    #endregion
}

#pragma warning restore CS0618
