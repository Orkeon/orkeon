using Orkeon.Domain.Tools.Protocol;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Tools.Protocol;

/// <summary>
/// Tests for ToolCallResponse following Clean Architecture principles.
/// Tests the tool call response record for tool execution results.
/// </summary>
public class ToolCallResponseTests
{
    private static readonly string[] Item1Item2Item3 = ["item1", "item2", "item3"];
    private static readonly int[] Int12345 = [1, 2, 3, 4, 5];
    private static readonly string[] Meta1Meta2 = ["meta1", "meta2"];
    private static readonly string[] FailedItems = ["item9", "item10"];
    #region Constructor and Basic Property Tests

    [Fact]
    public void ShouldCreateValidResponse_WhenConstructingWithSuccessfulResponse()
    {
        // Arrange
        var success = true;
        var result = "File content successfully read";
        var error = (string?)null;

        // Act
        var response = new ToolCallResponse(success, result, error);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(result, response!.Result);
        Assert.Null(response.Error);
        Assert.Null(response.Metadata);
    }

    [Fact]
    public void ShouldCreateValidResponse_WhenConstructingWithFailedResponse()
    {
        // Arrange
        var success = false;
        var result = (object?)null;
        var error = "File not found: /path/to/missing/file.txt";

        // Act
        var response = new ToolCallResponse(success, result, error);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response!.Result);
        Assert.Equal(error, response.Error);
        Assert.Null(response.Metadata);
    }

    [Fact]
    public void ShouldCreateValidResponse_WhenConstructingWithAllParameters()
    {
        // Arrange
        var success = true;
        var result = new { Data = "processed data", Count = 42 };
        var error = (string?)null;
        var metadata = new Dictionary<string, object?>
        {
            { "executionTime", 150 },
            { "memoryUsed", "2.5MB" },
            { "cacheHit", true }
        };

        // Act
        var response = new ToolCallResponse(success, result, error, metadata);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(result, response!.Result);
        Assert.Null(response.Error);
        Assert.Equal(metadata, response.Metadata);
        Assert.Equal(3, response.Metadata!.Count);
    }

    [Fact]
    public void ShouldAcceptNull_WhenConstructingWithNullMetadata()
    {
        // Arrange
        var success = true;
        var result = "Success result";
        var error = (string?)null;

        // Act
        var response = new ToolCallResponse(success, result, error, null);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(result, response!.Result);
        Assert.Null(response.Error);
        Assert.Null(response.Metadata);
    }

    [Fact]
    public void ShouldAcceptEmptyDictionary_WhenConstructingWithEmptyMetadata()
    {
        // Arrange
        var success = false;
        var result = (object?)null;
        var error = "Operation failed";
        var emptyMetadata = new Dictionary<string, object?>();

        // Act
        var response = new ToolCallResponse(success, result, error, emptyMetadata);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response!.Result);
        Assert.Equal(error, response.Error);
        Assert.Equal(emptyMetadata, response.Metadata);
        Assert.NotNull(response.Metadata);
        Assert.Empty(response.Metadata);
    }

    #endregion

    #region Result Type Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResultWithStringValue()
    {
        // Arrange & Act
        var response = new ToolCallResponse(true, "String result", null);

        // Assert
        Assert.IsType<string>(response!.Result);
        Assert.Equal("String result", response!.Result);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResultWithNumericValues()
    {
        // Arrange & Act
        var intResponse = new ToolCallResponse(true, 42, null);
        var doubleResponse = new ToolCallResponse(true, 3.14, null);
        var decimalResponse = new ToolCallResponse(true, 99.99m, null);

        // Assert
        Assert.IsType<int>(intResponse.Result);
        Assert.Equal(42, intResponse.Result);

        Assert.IsType<double>(doubleResponse.Result);
        Assert.Equal(3.14, doubleResponse.Result);

        Assert.IsType<decimal>(decimalResponse.Result);
        Assert.Equal(99.99m, decimalResponse.Result);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResultWithBooleanValue()
    {
        // Arrange & Act
        var trueResponse = new ToolCallResponse(true, true, null);
        var falseResponse = new ToolCallResponse(true, false, null);

        // Assert
        Assert.IsType<bool>(trueResponse.Result);
        Assert.True((bool)trueResponse.Result!);

        Assert.IsType<bool>(falseResponse.Result);
        Assert.False((bool)falseResponse.Result!);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResultWithComplexObjects()
    {
        // Arrange
        var complexResult = new
        {
            Name = "Test Result",
            Value = 123,
            Items = Item1Item2Item3,
            Metadata = new Dictionary<string, object?>
            {
                { "created", DateTime.UtcNow },
                { "valid", true }
            }
        };

        // Act
        var response = new ToolCallResponse(true, complexResult, null);

        // Assert
        Assert.NotNull(response!.Result);
        Assert.Equal(complexResult, response!.Result);

        // Access properties through dynamic
        var dynamicResult = (dynamic)response!.Result!;
        Assert.Equal("Test Result", dynamicResult.Name);
        Assert.Equal(123, dynamicResult.Value);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResultWithArrayAndList()
    {
        // Arrange
        var arrayResult = Int12345;
        var listResult = new List<string> { "apple", "banana", "cherry" };

        // Act
        var arrayResponse = new ToolCallResponse(true, arrayResult, null);
        var listResponse = new ToolCallResponse(true, listResult, null);

        // Assert
        Assert.IsType<int[]>(arrayResponse.Result);
        Assert.Equal(arrayResult, arrayResponse.Result);

        Assert.IsType<List<string>>(listResponse.Result);
        Assert.Equal(listResult, listResponse.Result);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingResultWithNullValue()
    {
        // Arrange & Act
        var nullResponse = new ToolCallResponse(true, null, null);

        // Assert
        Assert.Null(nullResponse.Result);
    }

    #endregion

    #region Error Handling Tests

    [Theory]
    [InlineData("File not found")]
    [InlineData("Network timeout occurred")]
    [InlineData("Invalid input parameters")]
    [InlineData("")]
    [InlineData(null)]
    public void ShouldAcceptAll_WhenUsingErrorWithVariousErrorMessages(string? error)
    {
        // Arrange & Act
        var response = new ToolCallResponse(false, null, error);

        // Assert
        Assert.Equal(error, response.Error);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingErrorWithDetailedErrorMessage()
    {
        // Arrange
        var detailedError = "Database connection failed: Timeout expired. " +
                           "The timeout period elapsed prior to completion of the operation " +
                           "or the server is not responding. (Connection timeout: 30 seconds)";

        // Act
        var response = new ToolCallResponse(false, null, detailedError);

        // Assert
        Assert.Equal(detailedError, response.Error);
        Assert.False(response.Success);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingErrorWithSpecialCharacters()
    {
        // Arrange
        var specialError = "Error with special chars: !@#$%^&*()[]{}|\\:;\"'<>,.?/~`";

        // Act
        var response = new ToolCallResponse(false, null, specialError);

        // Assert
        Assert.Equal(specialError, response.Error);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingErrorWithUnicodeCharacters()
    {
        // Arrange
        var unicodeError = "Erreur avec des caractères spéciaux: émojis 🚨❌ et UTF-8: 错误信息";

        // Act
        var response = new ToolCallResponse(false, null, unicodeError);

        // Assert
        Assert.Equal(unicodeError, response.Error);
    }

    #endregion

    #region Success/Error State Validation Tests

    [Fact]
    public void ShouldBeValid_WhenConstructingUsingSuccessWithResultNoError()
    {
        // Arrange & Act
        var response = new ToolCallResponse(true, "Success result", null);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response!.Result);
        Assert.Null(response.Error);
    }

    [Fact]
    public void ShouldBeValid_WhenConstructingUsingFailureWithErrorNoResult()
    {
        // Arrange & Act
        var response = new ToolCallResponse(false, null, "Error message");

        // Assert
        Assert.False(response.Success);
        Assert.Null(response!.Result);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void ShouldBeValid_WhenConstructingUsingSuccessWithBothResultAndError()
    {
        // Arrange & Act - This might happen in partial success scenarios
        var response = new ToolCallResponse(true, "Partial result", "Warning: Some data missing");

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response!.Result);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void ShouldBeValid_WhenConstructingUsingFailureWithBothResultAndError()
    {
        // Arrange & Act - This might happen when we have partial results even on failure
        var response = new ToolCallResponse(false, "Partial data before failure", "Connection lost");

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response!.Result);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void ShouldBeValid_WhenConstructingUsingSuccessWithNullResultAndError()
    {
        // Arrange & Act - Success with no meaningful result (e.g., delete operation)
        var response = new ToolCallResponse(true, null, null);

        // Assert
        Assert.True(response.Success);
        Assert.Null(response!.Result);
        Assert.Null(response.Error);
    }

    [Fact]
    public void ShouldBeValid_WhenConstructingUsingFailureWithNullResultAndError()
    {
        // Arrange & Act - Failure with no error message
        var response = new ToolCallResponse(false, null, null);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response!.Result);
        Assert.Null(response.Error);
    }

    #endregion

    #region Metadata Handling Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMetadataWithComplexObjects()
    {
        // Arrange
        var complexMetadata = new Dictionary<string, object?>
        {
            { "string", "metadata value" },
            { "int", 100 },
            { "bool", false },
            { "double", 2.71 },
            { "datetime", DateTime.UtcNow },
            { "array", Meta1Meta2 },
            { "nested", new { Type = "Performance", Score = 85 } },
            { "null", null! }
        };

        // Act
        var response = new ToolCallResponse(true, "Result", null, complexMetadata);

        // Assert
        Assert.Equal(8, response.Metadata!.Count);
        Assert.Equal("metadata value", response.Metadata["string"]);
        Assert.Equal(100, response.Metadata["int"]);
        Assert.False((bool)response.Metadata["bool"]!);
        Assert.Equal(2.71, response.Metadata["double"]);
        Assert.IsType<DateTime>(response.Metadata["datetime"]);
        Assert.IsType<string[]>(response.Metadata["array"]);
        Assert.Null(response.Metadata["null"]);
    }

    [Fact]
    public void ShouldAffectOriginalDictionary_WhenUsingMetadataModificationAfterCreation()
    {
        // Arrange
        var metadata = new Dictionary<string, object?> { { "initial", "value" } };
        var response = new ToolCallResponse(true, "Result", null, metadata);

        // Act
        metadata.Add("added", "after creation");

        // Assert
        Assert.Equal(2, response.Metadata!.Count);
        Assert.Contains("added", response.Metadata.Keys);
        Assert.Equal("after creation", response.Metadata["added"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMetadataWithPerformanceMetrics()
    {
        // Arrange
        var performanceMetadata = new Dictionary<string, object?>
        {
            { "executionTimeMs", 1250 },
            { "memoryUsedBytes", 2048576 },
            { "cpuUsagePercent", 15.5 },
            { "cacheHit", true },
            { "retryCount", 0 },
            { "networkLatencyMs", 45 }
        };

        // Act
        var response = new ToolCallResponse(true, "Operation completed", null, performanceMetadata);

        // Assert
        Assert.Equal(6, response.Metadata!.Count);
        Assert.Equal(1250, response.Metadata["executionTimeMs"]);
        Assert.Equal(2048576, response.Metadata["memoryUsedBytes"]);
        Assert.Equal(15.5, response.Metadata["cpuUsagePercent"]);
        Assert.True((bool)response.Metadata["cacheHit"]!);
    }

    #endregion

    #region Record Equality and HashCode Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var success = true;
        var result = "Test result";
        var error = (string?)null;
        var metadata = new Dictionary<string, object?> { { "key", "value" } };

        var response1 = new ToolCallResponse(success, result, error, metadata);
        var response2 = new ToolCallResponse(success, result, error, metadata);

        // Act & Assert
        Assert.Equal(response1, response2);
        Assert.True(response1.Equals(response2));
        Assert.True(response1 == response2);
        Assert.False(response1 != response2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentSuccess()
    {
        // Arrange
        var result = "Test result";
        var error = (string?)null;
        var metadata = new Dictionary<string, object?> { { "key", "value" } };

        var response1 = new ToolCallResponse(true, result, error, metadata);
        var response2 = new ToolCallResponse(false, result, error, metadata);

        // Act & Assert
        Assert.NotEqual(response1, response2);
        Assert.False(response1.Equals(response2));
        Assert.False(response1 == response2);
        Assert.True(response1 != response2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentResult()
    {
        // Arrange
        var success = true;
        var error = (string?)null;

        var response1 = new ToolCallResponse(success, "Result 1", error);
        var response2 = new ToolCallResponse(success, "Result 2", error);

        // Act & Assert
        Assert.NotEqual(response1, response2);
        Assert.False(response1.Equals(response2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentError()
    {
        // Arrange
        var success = false;
        var result = (object?)null;

        var response1 = new ToolCallResponse(success, result, "Error 1");
        var response2 = new ToolCallResponse(success, result, "Error 2");

        // Act & Assert
        Assert.NotEqual(response1, response2);
        Assert.False(response1.Equals(response2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentMetadata()
    {
        // Arrange
        var success = true;
        var result = "Test result";
        var error = (string?)null;
        var metadata1 = new Dictionary<string, object?> { { "key", "value1" } };
        var metadata2 = new Dictionary<string, object?> { { "key", "value2" } };

        var response1 = new ToolCallResponse(success, result, error, metadata1);
        var response2 = new ToolCallResponse(success, result, error, metadata2);

        // Act & Assert
        Assert.NotEqual(response1, response2);
        Assert.False(response1.Equals(response2));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var success = true;
        var result = "Test result";
        var error = (string?)null;
        var metadata = new Dictionary<string, object?> { { "key", "value" } };

        var response1 = new ToolCallResponse(success, result, error, metadata);
        var response2 = new ToolCallResponse(success, result, error, metadata);

        // Act
        var hash1 = response1.GetHashCode();
        var hash2 = response2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var response1 = new ToolCallResponse(true, "Result 1", null);
        var response2 = new ToolCallResponse(true, "Result 2", null);

        // Act
        var hash1 = response1.GetHashCode();
        var hash2 = response2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Record Deconstruction Tests

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingDeconstruct()
    {
        // Arrange
        var success = true;
        var result = "Test result";
        var error = (string?)null;
        var metadata = new Dictionary<string, object?> { { "key", "value" } };

        var response = new ToolCallResponse(success, result, error, metadata);

        // Act
        var (extractedSuccess, extractedResult, extractedError, extractedMetadata) = response;

        // Assert
        Assert.Equal(success, extractedSuccess);
        Assert.Equal(result, extractedResult);
        Assert.Equal(error, extractedError);
        Assert.Equal(metadata, extractedMetadata);
    }

    [Fact]
    public void ShouldExtractNulls_WhenUsingDeconstructWithNullValues()
    {
        // Arrange
        var response = new ToolCallResponse(false, null, null, null);

        // Act
        var (success, result, error, metadata) = response;

        // Assert
        Assert.False(success);
        Assert.Null(result);
        Assert.Null(error);
        Assert.Null(metadata);
    }

    #endregion

    #region Record With Expression Tests

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedSuccess_WhenUsingWithModifySuccess()
    {
        // Arrange
        var originalResponse = new ToolCallResponse(true, "Result", null,
            new Dictionary<string, object?> { { "key", "value" } });

        // Act
        var modifiedResponse = originalResponse with { Success = false };

        // Assert
        Assert.True(originalResponse.Success);
        Assert.False(modifiedResponse.Success);
        Assert.Equal(originalResponse.Result, modifiedResponse.Result);
        Assert.Equal(originalResponse.Error, modifiedResponse.Error);
        Assert.Equal(originalResponse.Metadata, modifiedResponse.Metadata);
        Assert.NotEqual(originalResponse, modifiedResponse);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedResult_WhenUsingWithModifyResult()
    {
        // Arrange
        var originalResponse = new ToolCallResponse(true, "Original result", null);

        // Act
        var modifiedResponse = originalResponse with { Result = "Modified result" };

        // Assert
        Assert.Equal("Original result", originalResponse.Result);
        Assert.Equal("Modified result", modifiedResponse.Result);
        Assert.Equal(originalResponse.Success, modifiedResponse.Success);
        Assert.NotEqual(originalResponse, modifiedResponse);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedError_WhenUsingWithModifyError()
    {
        // Arrange
        var originalResponse = new ToolCallResponse(false, null, "Original error");

        // Act
        var modifiedResponse = originalResponse with { Error = "Modified error" };

        // Assert
        Assert.Equal("Original error", originalResponse.Error);
        Assert.Equal("Modified error", modifiedResponse.Error);
        Assert.Equal(originalResponse.Success, modifiedResponse.Success);
        Assert.NotEqual(originalResponse, modifiedResponse);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithMetadata_WhenUsingWithAddMetadata()
    {
        // Arrange
        var originalResponse = new ToolCallResponse(true, "Result", null);
        var metadata = new Dictionary<string, object?> { { "added", "metadata" } };

        // Act
        var modifiedResponse = originalResponse with { Metadata = metadata };

        // Assert
        Assert.Null(originalResponse.Metadata);
        Assert.Equal(metadata, modifiedResponse.Metadata);
        Assert.NotEqual(originalResponse, modifiedResponse);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithAllUpdates_WhenUsingWithModifyMultipleProperties()
    {
        // Arrange
        var originalResponse = new ToolCallResponse(true, "Original", null, null);
        var newMetadata = new Dictionary<string, object?> { { "new", "metadata" } };

        // Act
        var modifiedResponse = originalResponse with
        {
            Success = false,
            Result = "New result",
            Error = "New error",
            Metadata = newMetadata
        };

        // Assert
        Assert.False(modifiedResponse.Success);
        Assert.Equal("New result", modifiedResponse.Result);
        Assert.Equal("New error", modifiedResponse.Error);
        Assert.Equal(newMetadata, modifiedResponse.Metadata);
        Assert.NotEqual(originalResponse, modifiedResponse);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingToolCallResponseInCollection()
    {
        // Arrange
        var responses = new List<ToolCallResponse>
        {
            new(true, "Success 1", null),
            new(false, null, "Error 1"),
            new(true, "Success 2", null),
            new(false, null, "Error 2"),
            new(true, null, null) // Success with no result
        };

        // Act
        var successfulResponses = responses.Where(r => r.Success).ToList();
        var failedResponses = responses.Where(r => !r.Success).ToList();
        var responsesWithResults = responses.Where(r => r.Result != null).ToList();
        var responsesWithErrors = responses.Where(r => r.Error != null).ToList();

        // Assert
        Assert.Equal(5, responses.Count);
        Assert.Equal(3, successfulResponses.Count);
        Assert.Equal(2, failedResponses.Count);
        Assert.Equal(2, responsesWithResults.Count);
        Assert.Equal(2, responsesWithErrors.Count);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingToolCallResponseInHashSet()
    {
        // Arrange
        var response1 = new ToolCallResponse(true, "Result", null);
        var response2 = new ToolCallResponse(true, "Result", null); // Identical
        var response3 = new ToolCallResponse(false, "Result", "Error");

        var hashSet = new HashSet<ToolCallResponse>
        {
            // Act
            response1,
            response2, // Should be treated as duplicate
            response3
        };

        // Assert
        Assert.Equal(2, hashSet.Count); // response1 and response2 are equal
        Assert.Contains(response1, hashSet);
        Assert.Contains(response3, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingToolCallResponseInDictionary()
    {
        // Arrange
        var response1 = new ToolCallResponse(true, Success, null);
        var response2 = new ToolCallResponse(false, null, Failed);

        var dictionary = new Dictionary<ToolCallResponse, string>
        {
            { response1, "Processed successfully" },
            { response2, "Needs retry" }
        };

        // Act & Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Processed successfully", dictionary[response1]);
        Assert.Equal("Needs retry", dictionary[response2]);
        Assert.True(dictionary.ContainsKey(response1));
        Assert.True(dictionary.ContainsKey(response2));
    }

    #endregion

    #region Business Logic and Semantic Tests

    [Fact]
    public void ShouldContainExpectedResult_WhenUsingToolCallResponseUsingSuccessfulFileOperation()
    {
        // Arrange & Act
        var fileReadResponse = new ToolCallResponse(
            true,
            "File content here...",
            null,
            new Dictionary<string, object?>
            {
                { "bytesRead", 1024 },
                { "encoding", "utf-8" },
                { "lastModified", DateTime.UtcNow }
            });

        var fileWriteResponse = new ToolCallResponse(
            true,
            null, // Write operations typically don't return content
            null,
            new Dictionary<string, object?>
            {
                { "bytesWritten", 512 },
                { "filePath", "/output/data.txt" }
            });

        // Assert
        Assert.True(fileReadResponse.Success);
        Assert.IsType<string>(fileReadResponse.Result);
        Assert.Contains("bytesRead", fileReadResponse.Metadata!.Keys);

        Assert.True(fileWriteResponse.Success);
        Assert.Null(fileWriteResponse.Result);
        Assert.Contains("bytesWritten", fileWriteResponse.Metadata!.Keys);
    }

    [Fact]
    public void ShouldContainExpectedError_WhenUsingToolCallResponseUsingFailedNetworkOperation()
    {
        // Arrange & Act
        var networkErrorResponse = new ToolCallResponse(
            false,
            null,
            "Network timeout: Unable to connect to remote server after 30 seconds",
            new Dictionary<string, object?>
            {
                { "attemptCount", 3 },
                { "lastAttemptTime", DateTime.UtcNow },
                { "errorCode", "NETWORK_TIMEOUT" }
            });

        // Assert
        Assert.False(networkErrorResponse.Success);
        Assert.Null(networkErrorResponse.Result);
        Assert.Contains("timeout", networkErrorResponse.Error!.ToLower());
        Assert.Equal(3, networkErrorResponse.Metadata!["attemptCount"]);
        Assert.Equal("NETWORK_TIMEOUT", networkErrorResponse.Metadata["errorCode"]);
    }

    [Theory]
    [InlineData(true, "Operation completed successfully")]
    [InlineData(false, "Operation failed with validation error")]
    public void ShouldReflectCorrectState_WhenUsingToolCallResponseWithDifferentOutcomes(bool success, string message)
    {
        // Arrange & Act
        var response = success
            ? new ToolCallResponse(true, message, null)
            : new ToolCallResponse(false, null, message);

        // Assert
        Assert.Equal(success, response.Success);
        if (success)
        {
            Assert.Equal(message, response!.Result);
            Assert.Null(response.Error);
        }
        else
        {
            Assert.Null(response!.Result);
            Assert.Equal(message, response.Error);
        }
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToolCallResponseWithLargeResult()
    {
        // Arrange
        var largeResult = new string('X', 100000); // 100KB string

        // Act
        var response = new ToolCallResponse(true, largeResult, null);

        // Assert
        Assert.Equal(100000, ((string)response!.Result!).Length);
        Assert.Equal(largeResult, response!.Result);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingToolCallResponseToString()
    {
        // Arrange
        var response = new ToolCallResponse(true, "Test result", null,
            new Dictionary<string, object?> { { "key", "value" } });

        // Act
        var stringRepresentation = response.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("ToolCallResponse", stringRepresentation);
    }

    [Fact]
    public void ShouldHandleBothResultAndError_WhenUsingToolCallResponseWithPartialSuccessScenario()
    {
        // Arrange & Act - Scenario where operation partially succeeds
        var partialResponse = new ToolCallResponse(
            true, // Overall success
            new { ProcessedItems = 8, TotalItems = 10 }, // Partial results
            "Warning: 2 items could not be processed due to validation errors", // Warning message
            new Dictionary<string, object?>
            {
                { "warningCount", 2 },
                { "successCount", 8 },
                { "failedItems", FailedItems }
            });

        // Assert
        Assert.True(partialResponse.Success);
        Assert.NotNull(partialResponse.Result);
        Assert.NotNull(partialResponse.Error); // Contains warning
        Assert.Contains("Warning", partialResponse.Error);
        Assert.Equal(2, partialResponse.Metadata!["warningCount"]);
    }

    [Fact]
    public void ShouldMaintainIndependence_WhenUsingToolCallResponseUsingChainedOperations()
    {
        // Arrange - Simulate tool response chain
        var step1Response = new ToolCallResponse(true, "Raw data extracted", null,
            new Dictionary<string, object?> { { "recordCount", 1000 } });

        var step2Response = new ToolCallResponse(true, "Data transformed", null,
            new Dictionary<string, object?> { { "transformedCount", 950 }, { "skippedCount", 50 } });

        var step3Response = new ToolCallResponse(false, null, "Database connection failed",
            new Dictionary<string, object?> { { "retryAfterSeconds", 60 } });

        // Act - Verify each response is independent
        var allResponses = new[] { step1Response, step2Response, step3Response };

        // Assert
        Assert.Equal(3, allResponses.Length);
        Assert.True(step1Response.Success);
        Assert.True(step2Response.Success);
        Assert.False(step3Response.Success);

        // Verify they are all different
        Assert.NotEqual(step1Response, step2Response);
        Assert.NotEqual(step2Response, step3Response);
        Assert.NotEqual(step1Response, step3Response);
    }

    #endregion
}
