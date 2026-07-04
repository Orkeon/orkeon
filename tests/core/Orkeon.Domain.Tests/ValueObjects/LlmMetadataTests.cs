using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmMetadataTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldCreateInstanceWithNullValues_WhenConstructingWithNoParameters()
    {
        // Act
        var metadata = new LlmMetadata();

        // Assert
        Assert.Null(metadata.Error);
        Assert.Null(metadata.TokensUsed);
        Assert.Null(metadata.TokensLimit);
        Assert.Null(metadata.ResponseTime);
        Assert.Null(metadata.Model);
        Assert.Null(metadata.Temperature);
        Assert.Null(metadata.CustomFields);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithAllParameters()
    {
        // Arrange
        var error = "Rate limit exceeded";
        var tokensUsed = 1500;
        var tokensLimit = 2000;
        var responseTime = TimeSpan.FromSeconds(2.5);
        var model = ModelGpt4;
        var temperature = 0.7;
        var customFields = new Dictionary<string, string>
        {
            { "provider", "OpenAI" },
            { "region", "us-east-1" }
        };

        // Act
        var metadata = new LlmMetadata(
            error,
            tokensUsed,
            tokensLimit,
            responseTime,
            model,
            temperature,
            customFields);

        // Assert
        Assert.Equal(error, metadata.Error);
        Assert.Equal(tokensUsed, metadata.TokensUsed);
        Assert.Equal(tokensLimit, metadata.TokensLimit);
        Assert.Equal(responseTime, metadata.ResponseTime);
        Assert.Equal(model, metadata.Model);
        Assert.Equal(temperature, metadata.Temperature);
        Assert.Equal(customFields, metadata.CustomFields);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithPartialParameters()
    {
        // Act
        var metadata = new LlmMetadata(
            Model: ModelClaude3,
            TokensUsed: 500);

        // Assert
        Assert.Null(metadata.Error);
        Assert.Equal(500, metadata.TokensUsed);
        Assert.Null(metadata.TokensLimit);
        Assert.Null(metadata.ResponseTime);
        Assert.Equal(ModelClaude3, metadata.Model);
        Assert.Null(metadata.Temperature);
        Assert.Null(metadata.CustomFields);
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var customFields = new Dictionary<string, string> { { "key", "value" } };
        var metadata1 = new LlmMetadata(
            Error: "Error",
            TokensUsed: 100,
            TokensLimit: 1000,
            ResponseTime: TimeSpan.FromSeconds(1),
            Model: "gpt-3.5",
            Temperature: 0.5,
            CustomFields: customFields);

        var metadata2 = new LlmMetadata(
            Error: "Error",
            TokensUsed: 100,
            TokensLimit: 1000,
            ResponseTime: TimeSpan.FromSeconds(1),
            Model: "gpt-3.5",
            Temperature: 0.5,
            CustomFields: customFields);

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1 == metadata2);
        Assert.False(metadata1 != metadata2);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var metadata1 = new LlmMetadata(TokensUsed: 100);
        var metadata2 = new LlmMetadata(TokensUsed: 200);

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
        Assert.False(metadata1 == metadata2);
        Assert.True(metadata1 != metadata2);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenRecordingEqualityWithNullValues()
    {
        // Arrange
        var metadata1 = new LlmMetadata();
        var metadata2 = new LlmMetadata();

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1 == metadata2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = new LlmMetadata(
            Model: ModelGpt4,
            TokensUsed: 100,
            Temperature: 0.7);

        // Act
        var modified = original with { TokensUsed = 200 };

        // Assert
        Assert.Equal(100, original.TokensUsed);
        Assert.Equal(200, modified.TokensUsed);
        Assert.Equal(original.Model, modified.Model);
        Assert.Equal(original.Temperature, modified.Temperature);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var metadata = new LlmMetadata(
            Model: ModelClaude3,
            TokensUsed: 1500,
            TokensLimit: 2000,
            Temperature: 0.8);

        // Act
        var result = metadata.ToJson();

        // Assert
        Assert.Contains(ModelClaude3, result);
        Assert.Contains("1500", result);
        Assert.Contains("2000", result);
        Assert.Contains("0.8", result);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingDeconstruct()
    {
        // Arrange
        var customFields = new Dictionary<string, string> { { "test", "value" } };
        var metadata = new LlmMetadata(
            Error: "test error",
            TokensUsed: 500,
            TokensLimit: 1000,
            ResponseTime: TimeSpan.FromSeconds(3),
            Model: ModelGpt4,
            Temperature: 0.9,
            CustomFields: customFields);

        // Act
        var (error, tokensUsed, tokensLimit, responseTime, model, temperature, fields) = metadata;

        // Assert
        Assert.Equal("test error", error);
        Assert.Equal(500, tokensUsed);
        Assert.Equal(1000, tokensLimit);
        Assert.Equal(TimeSpan.FromSeconds(3), responseTime);
        Assert.Equal(ModelGpt4, model);
        Assert.Equal(0.9, temperature);
        Assert.Equal(customFields, fields);
    }

    #endregion

    #region Scenario Tests

    [Fact]
    public void ShouldSuccessfulLlmCall_WhenUsingScenario()
    {
        // Simulate metadata for a successful LLM call
        var metadata = new LlmMetadata(
            TokensUsed: 750,
            TokensLimit: 4000,
            ResponseTime: TimeSpan.FromMilliseconds(1250),
            Model: ModelGpt4Turbo,
            Temperature: 0.7,
            CustomFields: new Dictionary<string, string>
            {
                { "request_id", "req_12345" },
                { "finish_reason", "stop" }
            });

        // Assert
        Assert.Null(metadata.Error);
        Assert.Equal(750, metadata.TokensUsed);
        Assert.Equal(4000, metadata.TokensLimit);
        Assert.Equal(1.25, metadata.ResponseTime?.TotalSeconds);
        Assert.Equal(ModelGpt4Turbo, metadata.Model);
        Assert.Equal(0.7, metadata.Temperature);
        Assert.Equal("req_12345", metadata.CustomFields?["request_id"]);
        Assert.Equal("stop", metadata.CustomFields?["finish_reason"]);
    }

    [Fact]
    public void ShouldFailedLlmCall_WhenUsingScenario()
    {
        // Simulate metadata for a failed LLM call
        var metadata = new LlmMetadata(
            Error: "API rate limit exceeded. Please retry after 60 seconds.",
            ResponseTime: TimeSpan.FromMilliseconds(150),
            Model: ModelGpt35Turbo,
            CustomFields: new Dictionary<string, string>
            {
                { "error_code", "rate_limit_exceeded" },
                { "retry_after", "60" }
            });

        // Assert
        Assert.NotNull(metadata.Error);
        Assert.Contains("rate limit", metadata.Error);
        Assert.Null(metadata.TokensUsed);
        Assert.Null(metadata.TokensLimit);
        Assert.Equal(0.15, metadata.ResponseTime?.TotalSeconds);
        Assert.Equal("rate_limit_exceeded", metadata.CustomFields?["error_code"]);
        Assert.Equal("60", metadata.CustomFields?["retry_after"]);
    }

    [Fact]
    public void ShouldStreamingResponse_WhenUsingScenario()
    {
        // Simulate metadata for a streaming response
        var metadata = new LlmMetadata(
            TokensUsed: 2500,
            TokensLimit: 8000,
            ResponseTime: TimeSpan.FromSeconds(5.5),
            Model: "claude-3-sonnet",
            Temperature: 0.3,
            CustomFields: new Dictionary<string, string>
            {
                { "stream", "true" },
                { "chunks_received", "45" },
                { "first_token_time", "0.250" }
            });

        // Assert
        Assert.Equal(2500, metadata.TokensUsed);
        Assert.True(metadata.TokensUsed < metadata.TokensLimit);
        Assert.Equal(5.5, metadata.ResponseTime?.TotalSeconds);
        Assert.Equal("true", metadata.CustomFields?["stream"]);
        Assert.Equal("45", metadata.CustomFields?["chunks_received"]);
    }

    [Fact]
    public void ShouldTokenLimitApproaching_WhenUsingScenario()
    {
        // Simulate metadata when approaching token limit
        var metadata = new LlmMetadata(
            TokensUsed: 3950,
            TokensLimit: 4000,
            ResponseTime: TimeSpan.FromSeconds(3.2),
            Model: ModelGpt4,
            Temperature: 0.5);

        // Calculate usage percentage
        var usagePercentage = (metadata.TokensUsed!.Value / (double)metadata.TokensLimit!.Value) * 100;

        // Assert
        Assert.True(usagePercentage > 95);
        Assert.True(metadata.TokensLimit - metadata.TokensUsed < 100);
    }

    [Fact]
    public void ShouldModifyingMetadata_WhenUsingScenario()
    {
        // Start with basic metadata
        var initial = new LlmMetadata(
            Model: ModelGpt35Turbo,
            TokensUsed: 100);

        // Add error information
        var withError = initial with
        {
            Error = "Context length exceeded",
            CustomFields = new Dictionary<string, string> { { "error_type", "context_length" } }
        };

        // Clear error and update tokens
        var recovered = withError with
        {
            Error = null,
            TokensUsed = 80,
            ResponseTime = TimeSpan.FromSeconds(1.5)
        };

        // Assert
        Assert.Null(initial.Error);
        Assert.NotNull(withError.Error);
        Assert.Null(recovered.Error);
        Assert.Equal(80, recovered.TokensUsed);
        Assert.NotNull(recovered.ResponseTime);
    }

    [Fact]
    public void ShouldAggregatingMultipleResponses_WhenUsingScenario()
    {
        // Simulate aggregating metadata from multiple LLM calls
        var responses = new[]
        {
            new LlmMetadata(TokensUsed: 500, ResponseTime: TimeSpan.FromSeconds(1.2)),
            new LlmMetadata(TokensUsed: 750, ResponseTime: TimeSpan.FromSeconds(1.8)),
            new LlmMetadata(TokensUsed: 600, ResponseTime: TimeSpan.FromSeconds(1.5))
        };

        // Calculate aggregates
        var totalTokens = responses.Where(r => r.TokensUsed.HasValue).Sum(r => r.TokensUsed!.Value);
        var avgResponseTime = responses
            .Where(r => r.ResponseTime.HasValue)
            .Average(r => r.ResponseTime!.Value.TotalSeconds);

        // Create aggregate metadata
        var aggregated = new LlmMetadata(
            TokensUsed: totalTokens,
            ResponseTime: TimeSpan.FromSeconds(avgResponseTime),
            CustomFields: new Dictionary<string, string>
            {
                { "total_requests", "3" },
                { "aggregation_type", "sum_and_average" }
            });

        // Assert
        Assert.Equal(1850, aggregated.TokensUsed);
        Assert.Equal(1.5, aggregated.ResponseTime?.TotalSeconds);
        Assert.Equal("3", aggregated.CustomFields?["total_requests"]);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldEmptyCustomFields_WhenUsingEdgeCase()
    {
        // Act
        var metadata = new LlmMetadata(
            Model: TestModelName,
            CustomFields: []);

        // Assert
        Assert.NotNull(metadata.CustomFields);
        Assert.Empty(metadata.CustomFields);
    }

    [Fact]
    public void ShouldZeroTokensUsed_WhenUsingEdgeCase()
    {
        // Act
        var metadata = new LlmMetadata(
            TokensUsed: 0,
            TokensLimit: 1000);

        // Assert
        Assert.Equal(0, metadata.TokensUsed);
        Assert.True(metadata.TokensUsed < metadata.TokensLimit);
    }

    [Fact]
    public void ShouldRejectNegativeTokensUsed_WhenUsingEdgeCase()
    {
        // Validation now rejects negative TokensUsed
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(TokensUsed: -100));
    }

    [Fact]
    public void ShouldRejectNegativeTokensLimit_WhenUsingEdgeCase()
    {
        // Validation now rejects negative TokensLimit
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(TokensLimit: -1000));
    }

    [Fact]
    public void ShouldZeroResponseTime_WhenUsingEdgeCase()
    {
        // Act
        var metadata = new LlmMetadata(ResponseTime: TimeSpan.Zero);

        // Assert
        Assert.Equal(TimeSpan.Zero, metadata.ResponseTime);
        Assert.Equal(0, metadata.ResponseTime?.TotalMilliseconds);
    }

    [Fact]
    public void ShouldAcceptBoundaryTemperatureValues_WhenUsingEdgeCase()
    {
        // Test boundary temperature values
        var veryLow = new LlmMetadata(Temperature: 0.0);
        var veryHigh = new LlmMetadata(Temperature: 2.0);

        // Assert
        Assert.Equal(0.0, veryLow.Temperature);
        Assert.Equal(2.0, veryHigh.Temperature);
    }

    [Fact]
    public void ShouldRejectNegativeTemperature_WhenUsingEdgeCase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(Temperature: -0.5));
    }

    [Fact]
    public void ShouldRejectTemperatureAboveMax_WhenUsingEdgeCase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LlmMetadata(Temperature: 2.1));
    }

    [Fact]
    public void ShouldVeryLongErrorMessage_WhenUsingEdgeCase()
    {
        // Arrange
        var longError = string.Join(" ", Enumerable.Repeat("Error occurred.", 100));

        // Act
        var metadata = new LlmMetadata(Error: longError);

        // Assert
        Assert.Equal(longError, metadata.Error);
        Assert.True(metadata.Error!.Length > 1000);
    }

    [Fact]
    public void ShouldCustomFieldsWithSpecialCharacters_WhenUsingEdgeCase()
    {
        // Act
        var metadata = new LlmMetadata(
            CustomFields: new Dictionary<string, string>
            {
                { "key with spaces", "value with spaces" },
                { "key/with/slashes", "value/with/slashes" },
                { "key:with:colons", "value:with:colons" },
                { "key-with-dashes", "value-with-dashes" },
                { "", "empty key" },
                { "empty value", "" }
            });

        // Assert
        Assert.Equal(6, metadata.CustomFields?.Count);
        Assert.Equal("value with spaces", metadata.CustomFields?["key with spaces"]);
        Assert.Equal("empty key", metadata.CustomFields?[""]);
        Assert.Equal("", metadata.CustomFields?["empty value"]);
    }

    [Fact]
    public void ShouldModifyingWithAllNulls_WhenUsingEdgeCase()
    {
        // Arrange
        var original = new LlmMetadata(
            Error: "error",
            TokensUsed: 100,
            TokensLimit: 1000,
            ResponseTime: TimeSpan.FromSeconds(1),
            Model: "model",
            Temperature: 0.5,
            CustomFields: new Dictionary<string, string> { { "key", "value" } });

        // Act
        var cleared = original with
        {
            Error = null,
            TokensUsed = null,
            TokensLimit = null,
            ResponseTime = null,
            Model = null,
            Temperature = null,
            CustomFields = null
        };

        // Assert
        Assert.Null(cleared.Error);
        Assert.Null(cleared.TokensUsed);
        Assert.Null(cleared.TokensLimit);
        Assert.Null(cleared.ResponseTime);
        Assert.Null(cleared.Model);
        Assert.Null(cleared.Temperature);
        Assert.Null(cleared.CustomFields);
    }

    #endregion
}
