using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ContextWindowSettingsTests
{
    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingDefault()
    {
        // Act
        var settings = ContextWindowSettings.Default;

        // Assert
        Assert.Equal(4096, settings.MaxTokens);
        Assert.True(settings.AutoSummarize);
        Assert.Equal(0.5, settings.CompressionRatio);
        Assert.Equal("Summarize the following while preserving key information:", settings.SummaryPrompt);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenCreatingWithCustomValues()
    {
        // Arrange
        var maxTokens = 8000;
        var autoSummarize = false;
        var compressionRatio = 0.75;
        var summaryPrompt = "Custom summary prompt";

        // Act
        var settings = ContextWindowSettings.Create(
            maxTokens,
            autoSummarize,
            compressionRatio,
            summaryPrompt);

        // Assert
        Assert.Equal(maxTokens, settings.MaxTokens);
        Assert.Equal(autoSummarize, settings.AutoSummarize);
        Assert.Equal(compressionRatio, settings.CompressionRatio);
        Assert.Equal(summaryPrompt, settings.SummaryPrompt);
    }

    [Fact]
    public void ShouldUseDefaultsForOthers_WhenCreatingWithPartialValues()
    {
        // Act
        var settings1 = ContextWindowSettings.Create(maxTokens: 6000);
        var settings2 = ContextWindowSettings.Create(autoSummarize: false);
        var settings3 = ContextWindowSettings.Create(compressionRatio: 0.3);
        var settings4 = ContextWindowSettings.Create(summaryPrompt: "Brief summary:");

        // Assert
        Assert.Equal(6000, settings1.MaxTokens);
        Assert.True(settings1.AutoSummarize); // Default
        Assert.Equal(0.5, settings1.CompressionRatio); // Default

        Assert.Equal(4096, settings2.MaxTokens); // Default
        Assert.False(settings2.AutoSummarize);
        Assert.Equal(0.5, settings2.CompressionRatio); // Default

        Assert.Equal(4096, settings3.MaxTokens); // Default
        Assert.True(settings3.AutoSummarize); // Default
        Assert.Equal(0.3, settings3.CompressionRatio);

        Assert.Equal(4096, settings4.MaxTokens); // Default
        Assert.True(settings4.AutoSummarize); // Default
        Assert.Equal("Brief summary:", settings4.SummaryPrompt);
    }

    [Fact]
    public void ShouldSupportEquality_WhenUsingContextWindowSettingsUsingAsRecord()
    {
        // Arrange
        var settings1 = ContextWindowSettings.Create(5000, false, 0.6, "Summary:");
        var settings2 = ContextWindowSettings.Create(5000, false, 0.6, "Summary:");
        var settings3 = ContextWindowSettings.Create(5000, true, 0.6, "Summary:");

        // Act & Assert
        Assert.Equal(settings1, settings2);
        Assert.NotEqual(settings1, settings3);
        Assert.Equal(settings1.GetHashCode(), settings2.GetHashCode());
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingContextWindowSettingsWithExpression()
    {
        // Arrange
        var original = ContextWindowSettings.Default;

        // Act
        var modified = original with { MaxTokens = 10000 };

        // Assert
        Assert.Equal(4096, original.MaxTokens); // Original unchanged
        Assert.Equal(10000, modified.MaxTokens); // Modified value
        Assert.Equal(original.AutoSummarize, modified.AutoSummarize); // Other values preserved
        Assert.Equal(original.CompressionRatio, modified.CompressionRatio);
        Assert.Equal(original.SummaryPrompt, modified.SummaryPrompt);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingContextWindowSettingsWithMultipleWithExpressions()
    {
        // Arrange
        var original = ContextWindowSettings.Default;

        // Act
        var modified = original with
        {
            MaxTokens = 12000,
            AutoSummarize = false,
            CompressionRatio = 0.8
        };

        // Assert
        Assert.Equal(12000, modified.MaxTokens);
        Assert.False(modified.AutoSummarize);
        Assert.Equal(0.8, modified.CompressionRatio);
        Assert.Equal(original.SummaryPrompt, modified.SummaryPrompt); // Unchanged
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(16000)]
    [InlineData(32000)]
    [InlineData(int.MaxValue)]
    public void ShouldAcceptVariousValues_WhenUsingMaxTokens(int maxTokens)
    {
        // Act
        var settings = ContextWindowSettings.Create(maxTokens: maxTokens);

        // Assert
        Assert.Equal(maxTokens, settings.MaxTokens);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ShouldAcceptVariousValues_WhenUsingCompressionRatio(double ratio)
    {
        // Act
        var settings = ContextWindowSettings.Create(compressionRatio: ratio);

        // Assert
        Assert.Equal(ratio, settings.CompressionRatio);
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    public void ShouldThrow_WhenUsingInvalidMaxTokens(int maxTokens)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextWindowSettings.Create(maxTokens: maxTokens));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void ShouldThrow_WhenUsingCompressionRatioOutOfRange(double ratio)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextWindowSettings.Create(compressionRatio: ratio));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ShouldThrow_WhenUsingSummaryPromptNullOrWhiteSpace(string prompt)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
            ContextWindowSettings.Create(summaryPrompt: prompt));
    }

    [Theory]
    [InlineData("Short prompt")]
    [InlineData("This is a very long summary prompt that contains detailed instructions for how to summarize the content while preserving all the important information")]
    public void ShouldAcceptVariousStrings_WhenUsingSummaryPrompt(string prompt)
    {
        // Act
        var settings = ContextWindowSettings.Create(summaryPrompt: prompt);

        // Assert
        Assert.Equal(prompt, settings.SummaryPrompt);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(5000, false, 0.7, TestPrompt);

        // Act
        var result = settings.ToJson();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("5000", result);
        Assert.Contains("false", result);
        Assert.Contains("0.7", result);
        Assert.Contains(TestPrompt, result);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingDeconstruction()
    {
        // Arrange
        var settings = ContextWindowSettings.Create(8192, false, 0.4, "Custom prompt");

        // Act
        var (maxTokens, autoSummarize, compressionRatio, summaryPrompt) = settings;

        // Assert
        Assert.Equal(8192, maxTokens);
        Assert.False(autoSummarize);
        Assert.Equal(0.4, compressionRatio);
        Assert.Equal("Custom prompt", summaryPrompt);
    }
}
