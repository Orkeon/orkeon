using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Application.Tests.DTOs;

public class TokenUsageTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var tokenUsage = new TokenUsage();

        // Assert
        Assert.Equal(0, tokenUsage.PromptTokens);
        Assert.Equal(0, tokenUsage.CompletionTokens);
        Assert.Equal(0, tokenUsage.TotalTokens);
        Assert.Null(tokenUsage.EstimatedCost);
        Assert.Null(tokenUsage.Model);
        Assert.Null(tokenUsage.Provider);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingPromptTokens()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.PromptTokens = 150;

        // Assert
        Assert.Equal(150, tokenUsage.PromptTokens);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCompletionTokens()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.CompletionTokens = 75;

        // Assert
        Assert.Equal(75, tokenUsage.CompletionTokens);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTotalTokens()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.TotalTokens = 225;

        // Assert
        Assert.Equal(225, tokenUsage.TotalTokens);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEstimatedCost()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.EstimatedCost = 0.0045m;

        // Assert
        Assert.Equal(0.0045m, tokenUsage.EstimatedCost);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingModel()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.Model = ModelGpt4;

        // Assert
        Assert.Equal(ModelGpt4, tokenUsage.Model);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingProvider()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.Provider = "OpenAI";

        // Assert
        Assert.Equal("OpenAI", tokenUsage.Provider);
    }

    [Fact]
    public void ShouldBeValid_WhenAccessingAllPropertiesWithValidValues()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = 200,
            CompletionTokens = 150,
            TotalTokens = 350,
            EstimatedCost = 0.007m,
            Model = ModelGpt35Turbo,
            Provider = "OpenAI"
        };

        // Assert
        Assert.Equal(200, tokenUsage.PromptTokens);
        Assert.Equal(150, tokenUsage.CompletionTokens);
        Assert.Equal(350, tokenUsage.TotalTokens);
        Assert.Equal(0.007m, tokenUsage.EstimatedCost);
        Assert.Equal(ModelGpt35Turbo, tokenUsage.Model);
        Assert.Equal("OpenAI", tokenUsage.Provider);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPromptTokensWithZeroValue()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = 0
        };

        // Assert
        Assert.Equal(0, tokenUsage.PromptTokens);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompletionTokensWithZeroValue()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            CompletionTokens = 0
        };

        // Assert
        Assert.Equal(0, tokenUsage.CompletionTokens);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingTotalTokensWithZeroValue()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            TotalTokens = 0
        };

        // Assert
        Assert.Equal(0, tokenUsage.TotalTokens);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingEstimatedCostWithZeroValue()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            EstimatedCost = 0.0m
        };

        // Assert
        Assert.Equal(0.0m, tokenUsage.EstimatedCost);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingEstimatedCostWithNullValue()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            EstimatedCost = null
        };

        // Assert
        Assert.Null(tokenUsage.EstimatedCost);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingModelWithEmptyString()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            Model = string.Empty
        };

        // Assert
        Assert.Equal(string.Empty, tokenUsage.Model);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingProviderWithEmptyString()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            Provider = string.Empty
        };

        // Assert
        Assert.Equal(string.Empty, tokenUsage.Provider);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void ShouldBeValid_WhenUsingPromptTokensWithVariousValues(int tokens)
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = tokens
        };

        // Assert
        Assert.Equal(tokens, tokenUsage.PromptTokens);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(5000)]
    public void ShouldBeValid_WhenUsingCompletionTokensWithVariousValues(int tokens)
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            CompletionTokens = tokens
        };

        // Assert
        Assert.Equal(tokens, tokenUsage.CompletionTokens);
    }

    [Theory]
    [InlineData(ModelGpt35Turbo)]
    [InlineData(ModelGpt4)]
    [InlineData("claude-3-sonnet")]
    [InlineData("llama-2-70b")]
    public void ShouldBeValid_WhenUsingModelWithCommonModels(string model)
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            Model = model
        };

        // Assert
        Assert.Equal(model, tokenUsage.Model);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Ollama")]
    [InlineData("Hugging Face")]
    public void ShouldBeValid_WhenUsingProviderWithCommonProviders(string provider)
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            Provider = provider
        };

        // Assert
        Assert.Equal(provider, tokenUsage.Provider);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void ShouldBeValid_WhenUsingEstimatedCostWithVariousValues(decimal cost)
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            EstimatedCost = cost
        };

        // Assert
        Assert.Equal(cost, tokenUsage.EstimatedCost);
    }

    [Fact]
    public void ShouldBeAllowed_WhenTokenizingCountsWithNegativeValues()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = -10,
            CompletionTokens = -5,
            TotalTokens = -15
        };

        // Assert
        Assert.Equal(-10, tokenUsage.PromptTokens);
        Assert.Equal(-5, tokenUsage.CompletionTokens);
        Assert.Equal(-15, tokenUsage.TotalTokens);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingEstimatedCostWithNegativeValue()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            EstimatedCost = -0.005m
        };

        // Assert
        Assert.Equal(-0.005m, tokenUsage.EstimatedCost);
    }

    [Fact]
    public void ShouldTotalTokensShouldEqualSumOfPromptAndCompletion_WhenUsingConsistentCalculation()
    {
        // Arrange
        var promptTokens = 100;
        var completionTokens = 75;
        var expectedTotal = promptTokens + completionTokens;

        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = expectedTotal
        };

        // Assert
        Assert.Equal(expectedTotal, tokenUsage.TotalTokens);
        Assert.Equal(promptTokens + completionTokens, tokenUsage.TotalTokens);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingRealisticUsageScenarioOpeningAI()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = 2048,
            CompletionTokens = 1024,
            TotalTokens = 3072,
            EstimatedCost = 0.0614m, // Rough calculation for GPT-4
            Model = ModelGpt4,
            Provider = "OpenAI"
        };

        // Assert
        Assert.Equal(2048, tokenUsage.PromptTokens);
        Assert.Equal(1024, tokenUsage.CompletionTokens);
        Assert.Equal(3072, tokenUsage.TotalTokens);
        Assert.Equal(0.0614m, tokenUsage.EstimatedCost);
        Assert.Equal(ModelGpt4, tokenUsage.Model);
        Assert.Equal("OpenAI", tokenUsage.Provider);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingRealisticUsageScenarioUsingLocalLLM()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = 512,
            CompletionTokens = 256,
            TotalTokens = 768,
            EstimatedCost = null, // Local LLMs typically have no cost
            Model = "llama-2-7b",
            Provider = "Ollama"
        };

        // Assert
        Assert.Equal(512, tokenUsage.PromptTokens);
        Assert.Equal(256, tokenUsage.CompletionTokens);
        Assert.Equal(768, tokenUsage.TotalTokens);
        Assert.Null(tokenUsage.EstimatedCost);
        Assert.Equal("llama-2-7b", tokenUsage.Model);
        Assert.Equal("Ollama", tokenUsage.Provider);
    }

    [Fact]
    public void ShouldBeIndependent_WhenAccessingProperties()
    {
        // Arrange
        var tokenUsage = new TokenUsage();

        // Act
        tokenUsage.PromptTokens = 100;
        tokenUsage.CompletionTokens = 50;
        tokenUsage.TotalTokens = 175; // Intentionally not sum
        tokenUsage.EstimatedCost = 0.005m;
        tokenUsage.Model = CustomModelName;
        tokenUsage.Provider = "custom-provider";

        // Assert - Each property should maintain its individual value
        Assert.Equal(100, tokenUsage.PromptTokens);
        Assert.Equal(50, tokenUsage.CompletionTokens);
        Assert.Equal(175, tokenUsage.TotalTokens); // Not validated to be sum
        Assert.Equal(0.005m, tokenUsage.EstimatedCost);
        Assert.Equal(CustomModelName, tokenUsage.Model);
        Assert.Equal("custom-provider", tokenUsage.Provider);
    }

    [Fact]
    public void ShouldBeHandled_WhenUsingLargeTokenCounts()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            PromptTokens = int.MaxValue,
            CompletionTokens = int.MaxValue / 2,
            TotalTokens = int.MaxValue
        };

        // Assert
        Assert.Equal(int.MaxValue, tokenUsage.PromptTokens);
        Assert.Equal(int.MaxValue / 2, tokenUsage.CompletionTokens);
        Assert.Equal(int.MaxValue, tokenUsage.TotalTokens);
    }

    [Fact]
    public void ShouldBeHandled_WhenUsingHighPrecisionCost()
    {
        // Act
        var tokenUsage = new TokenUsage
        {
            EstimatedCost = 0.123456789m // High precision decimal
        };

        // Assert
        Assert.Equal(0.123456789m, tokenUsage.EstimatedCost);
    }
}
