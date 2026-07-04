using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Application.Tests.DTOs.Common;

public class LlmConfigDtoTests
{
    #region LlmConfigDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingLlmConfigDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4
        };

        // Assert
        Assert.Equal(ProviderOpenAI, dto.Provider);
        Assert.Equal(ModelGpt4, dto.Model);
        Assert.Equal(0.7, dto.Temperature); // Default value
        Assert.Null(dto.MaxTokens);
        Assert.Null(dto.TopP);
        Assert.Null(dto.FrequencyPenalty);
        Assert.Null(dto.PresencePenalty);
        Assert.Null(dto.ApiEndpoint);
        Assert.Null(dto.ApiKey);
        Assert.Equal(60, dto.TimeoutSeconds); // Default value
        Assert.Null(dto.MaxRequestsPerMinute);
        Assert.False(dto.EnableStreaming); // Default value
        Assert.Empty(dto.CustomSettings);
        Assert.Empty(dto.StopSequences);
        Assert.False(dto.EnableCaching); // Default value
        Assert.Equal(60, dto.CacheTtlMinutes); // Default value
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingLlmConfigDtoWithAllProperties()
    {
        // Arrange
        var customSettings = new Dictionary<string, object>
        {
            { "systemPrompt", "You are a helpful assistant" },
            { "maxRetries", 3 },
            { "logProbs", true }
        };

        var stopSequences = new List<string> { "\n\n", "###", "END" };

        // Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderAnthropic,
            Model = "claude-3-opus",
            Temperature = 0.9,
            MaxTokens = 4096,
            TopP = 0.95,
            FrequencyPenalty = 0.5,
            PresencePenalty = 0.3,
            ApiEndpoint = "https://api.anthropic.com/v1",
            ApiKey = "sk-test-key",
            TimeoutSeconds = 120,
            MaxRequestsPerMinute = 100,
            EnableStreaming = true,
            CustomSettings = customSettings,
            StopSequences = stopSequences,
            EnableCaching = true,
            CacheTtlMinutes = 120
        };

        // Assert
        Assert.Equal(ProviderAnthropic, dto.Provider);
        Assert.Equal("claude-3-opus", dto.Model);
        Assert.Equal(0.9, dto.Temperature);
        Assert.Equal(4096, dto.MaxTokens);
        Assert.Equal(0.95, dto.TopP);
        Assert.Equal(0.5, dto.FrequencyPenalty);
        Assert.Equal(0.3, dto.PresencePenalty);
        Assert.Equal("https://api.anthropic.com/v1", dto.ApiEndpoint);
        Assert.Equal("sk-test-key", dto.ApiKey);
        Assert.Equal(120, dto.TimeoutSeconds);
        Assert.Equal(100, dto.MaxRequestsPerMinute);
        Assert.True(dto.EnableStreaming);
        Assert.Equal(3, dto.CustomSettings.Count);
        Assert.Equal(3, dto.StopSequences.Count);
        Assert.True(dto.EnableCaching);
        Assert.Equal(120, dto.CacheTtlMinutes);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoUsingTemperature(double temperature)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            Temperature = temperature
        };

        // Assert
        Assert.Equal(temperature, dto.Temperature);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(50000)]
    [InlineData(128000)]
    [InlineData(200000)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoWithMaxTokens(int maxTokens)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            MaxTokens = maxTokens
        };

        // Assert
        Assert.Equal(maxTokens, dto.MaxTokens);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoUsingTopP(double topP)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            TopP = topP
        };

        // Assert
        Assert.Equal(topP, dto.TopP);
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoUsingFrequencyPenalty(double penalty)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            FrequencyPenalty = penalty
        };

        // Assert
        Assert.Equal(penalty, dto.FrequencyPenalty);
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoUsingPresencePenalty(double penalty)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            PresencePenalty = penalty
        };

        // Assert
        Assert.Equal(penalty, dto.PresencePenalty);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(300)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoUsingTimeoutSeconds(int timeout)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            TimeoutSeconds = timeout
        };

        // Assert
        Assert.Equal(timeout, dto.TimeoutSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoWithMaxRequestsPerMinute(int maxRequests)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            MaxRequestsPerMinute = maxRequests
        };

        // Assert
        Assert.Equal(maxRequests, dto.MaxRequestsPerMinute);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(720)]
    [InlineData(1440)]
    public void ShouldAcceptValidRange_WhenUsingLlmConfigDtoCachingTtlMinutes(int ttl)
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            EnableCaching = true,
            CacheTtlMinutes = ttl
        };

        // Assert
        Assert.Equal(ttl, dto.CacheTtlMinutes);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldWork_WhenUsingLlmConfigDtoRecordingEquality()
    {
        // Arrange
        var customSettings = new Dictionary<string, object> { { "setting1", "value1" } };
        var stopSequences = new List<string> { "stop1", "stop2" };

        var dto1 = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            Temperature = 0.8,
            MaxTokens = 2048,
            CustomSettings = customSettings,
            StopSequences = stopSequences
        };

        var dto2 = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = ModelGpt4,
            Temperature = 0.8,
            MaxTokens = 2048,
            CustomSettings = customSettings,
            StopSequences = stopSequences
        };

        var dto3 = new LlmConfigDto
        {
            Provider = ProviderAnthropic,
            Model = ModelClaude3,
            Temperature = 0.8,
            MaxTokens = 2048,
            CustomSettings = customSettings,
            StopSequences = stopSequences
        };

        // Act & Assert
        Assert.Equal(dto1, dto2);
        Assert.NotEqual(dto1, dto3);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteLlmConfigForOpenAI()
    {
        // Arrange & Act
        var dto = new LlmConfigDto
        {
            Provider = ProviderOpenAI,
            Model = "gpt-4-turbo-preview",
            Temperature = 0.8,
            MaxTokens = 4096,
            TopP = 0.9,
            FrequencyPenalty = 0.2,
            PresencePenalty = 0.1,
            ApiEndpoint = "https://api.openai.com/v1",
            ApiKey = "sk-prod-key",
            TimeoutSeconds = 90,
            MaxRequestsPerMinute = 60,
            EnableStreaming = true,
            CustomSettings = new Dictionary<string, object>
            {
                { "systemMessage", "You are an AI assistant specialized in coding" },
                { "seed", 42 },
                { "responseFormat", "json_object" }
            },
            StopSequences = ["```", "\nHuman:", "\nAssistant:"],
            EnableCaching = true,
            CacheTtlMinutes = 30
        };

        // Assert
        Assert.Equal(ProviderOpenAI, dto.Provider);
        Assert.Equal("gpt-4-turbo-preview", dto.Model);
        Assert.Equal(0.8, dto.Temperature);
        Assert.Equal(4096, dto.MaxTokens);
        Assert.Equal(0.9, dto.TopP);
        Assert.Equal(0.2, dto.FrequencyPenalty);
        Assert.Equal(0.1, dto.PresencePenalty);
        Assert.True(dto.EnableStreaming);
        Assert.Equal(3, dto.CustomSettings.Count);
        Assert.Equal(3, dto.StopSequences.Count);
        Assert.True(dto.EnableCaching);
    }

    #endregion
}
