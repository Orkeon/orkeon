using Orkeon.Domain.Tools.Protocol;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Tools.Protocol;

/// <summary>
/// Tests for ToolCallRequest following Clean Architecture principles.
/// Tests the tool call request record for tool invocation protocol.
/// </summary>
public class ToolCallRequestTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    #region Constructor and Basic Property Tests

    [Fact]
    public void ShouldCreateValidRequest_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var toolName = ToolFileRead;
        var parameters = new Dictionary<string, object?>
        {
            { "filePath", "/path/to/file.txt" },
            { "encoding", "utf-8" }
        };

        // Act
        var request = new ToolCallRequest(toolName, parameters);

        // Assert
        Assert.Equal(toolName, request.ToolName);
        Assert.Equal(parameters, request.Parameters);
        Assert.Null(request.Context);
    }

    [Fact]
    public void ShouldCreateValidRequest_WhenConstructingWithAllParameters()
    {
        // Arrange
        var toolName = ToolWebScrape;
        var parameters = new Dictionary<string, object?>
        {
            { ParamUrl, TestBaseUrl },
            { "selector", ".content" },
            { "timeout", 30 }
        };
        var context = "Scraping data for market analysis";

        // Act
        var request = new ToolCallRequest(toolName, parameters, context);

        // Assert
        Assert.Equal(toolName, request.ToolName);
        Assert.Equal(parameters, request.Parameters);
        Assert.Equal(context, request.Context);
    }

    [Fact]
    public void ShouldAcceptEmptyDictionary_WhenConstructingWithEmptyParameters()
    {
        // Arrange
        var toolName = "CurrentTime";
        var emptyParameters = new Dictionary<string, object?>();

        // Act
        var request = new ToolCallRequest(toolName, emptyParameters);

        // Assert
        Assert.Equal(toolName, request.ToolName);
        Assert.Equal(emptyParameters, request.Parameters);
        Assert.NotNull(request.Parameters);
        Assert.Empty(request.Parameters);
    }

    [Fact]
    public void ShouldAcceptNull_WhenConstructingWithNullContext()
    {
        // Arrange
        var toolName = "Calculator";
        var parameters = new Dictionary<string, object?> { { "operation", "add" } };

        // Act
        var request = new ToolCallRequest(toolName, parameters, null);

        // Assert
        Assert.Equal(toolName, request.ToolName);
        Assert.Equal(parameters, request.Parameters);
        Assert.Null(request.Context);
    }

    [Fact]
    public void ShouldAcceptEmptyString_WhenConstructingWithEmptyContext()
    {
        // Arrange
        var toolName = "DataAnalysis";
        var parameters = new Dictionary<string, object?> { { "dataset", "sales.csv" } };
        var emptyContext = string.Empty;

        // Act
        var request = new ToolCallRequest(toolName, parameters, emptyContext);

        // Assert
        Assert.Equal(emptyContext, request.Context);
        Assert.Equal(string.Empty, request.Context);
    }

    #endregion

    #region Parameter Validation Tests

    [Theory]
    [InlineData(ToolFileRead)]
    [InlineData(ToolWebScrape)]
    [InlineData("HttpClient")]
    [InlineData(ToolDatabaseQuery)]
    [InlineData("")]
    public void ShouldAcceptAll_WhenConstructingWithVariousToolNames(string toolName)
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { { "param", "value" } };

        // Act
        var request = new ToolCallRequest(toolName, parameters);

        // Assert
        Assert.Equal(toolName, request.ToolName);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingParametersWithComplexObjects()
    {
        // Arrange
        var complexParameters = new Dictionary<string, object?>
        {
            { "string", "test value" },
            { "int", 42 },
            { "bool", true },
            { "double", 3.14 },
            { "array", Int123 },
            { "datetime", DateTime.UtcNow },
            { "nested", new { Name = "Test", Value = 123 } },
            { "null", null! }
        };

        // Act
        var request = new ToolCallRequest("ComplexTool", complexParameters);

        // Assert
        Assert.Equal(8, request.Parameters.Count);
        Assert.Equal("test value", request.Parameters["string"]);
        Assert.Equal(42, request.Parameters["int"]);
        Assert.True((bool)request.Parameters["bool"]!);
        Assert.Equal(3.14, request.Parameters["double"]);
        Assert.IsType<int[]>(request.Parameters["array"]);
        Assert.IsType<DateTime>(request.Parameters["datetime"]);
        Assert.Null(request.Parameters["null"]);
    }

    [Fact]
    public void ShouldAffectOriginalDictionary_WhenUsingParametersModificationAfterCreation()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { { "initial", "value" } };
        var request = new ToolCallRequest("TestTool", parameters);

        // Act
        parameters.Add("added", "after creation");

        // Assert
        Assert.Equal(2, request.Parameters.Count);
        Assert.Contains("added", request.Parameters.Keys);
        Assert.Equal("after creation", request.Parameters["added"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingParametersWithNestedDictionaries()
    {
        // Arrange
        var nestedParameters = new Dictionary<string, object?>
        {
            { "config", new Dictionary<string, object?>
                {
                    { "timeout", 30 },
                    { "retries", 3 },
                    { "headers", new Dictionary<string, string>
                        {
                            { "Authorization", "Bearer token" },
                            { "Content-Type", "application/json" }
                        }
                    }
                }
            },
            { "data", new List<Dictionary<string, object?>>
                {
                    new() { { "id", 1 }, { "name", "Item 1" } },
                    new() { { "id", 2 }, { "name", "Item 2" } }
                }
            }
        };

        // Act
        var request = new ToolCallRequest("NestedTool", nestedParameters);

        // Assert
        Assert.Equal(2, request.Parameters.Count);
        Assert.IsType<Dictionary<string, object?>>(request.Parameters["config"]);
        Assert.IsType<List<Dictionary<string, object?>>>(request.Parameters["data"]);
    }

    #endregion

    #region Context Validation Tests

    [Theory]
    [InlineData("Simple context")]
    [InlineData("Context with special chars: !@#$%^&*()")]
    [InlineData("Context with émojis 🚀🎉 and ütf-8: 你好世界")]
    [InlineData("")]
    [InlineData(null)]
    public void ShouldAcceptAll_WhenConstructingWithVariousContextValues(string? context)
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "param", "value" } };

        // Act
        var request = new ToolCallRequest(toolName, parameters, context);

        // Assert
        Assert.Equal(context, request.Context);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingContextWithVeryLongString()
    {
        // Arrange
        var longContext = new string('A', 10000);
        var parameters = new Dictionary<string, object?> { { "param", "value" } };

        // Act
        var request = new ToolCallRequest("TestTool", parameters, longContext);

        // Assert
        Assert.Equal(10000, request.Context!.Length);
        Assert.Equal(longContext, request.Context);
    }

    #endregion

    #region Record Equality and HashCode Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "key", "value" } };
        var context = "Test context";

        var request1 = new ToolCallRequest(toolName, parameters, context);
        var request2 = new ToolCallRequest(toolName, parameters, context);

        // Act & Assert
        Assert.Equal(request1, request2);
        Assert.True(request1.Equals(request2));
        Assert.True(request1 == request2);
        Assert.False(request1 != request2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentToolName()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { { "key", "value" } };
        var context = "Test context";

        var request1 = new ToolCallRequest("Tool1", parameters, context);
        var request2 = new ToolCallRequest("Tool2", parameters, context);

        // Act & Assert
        Assert.NotEqual(request1, request2);
        Assert.False(request1.Equals(request2));
        Assert.False(request1 == request2);
        Assert.True(request1 != request2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentParameters()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters1 = new Dictionary<string, object?> { { "key", "value1" } };
        var parameters2 = new Dictionary<string, object?> { { "key", "value2" } };
        var context = "Test context";

        var request1 = new ToolCallRequest(toolName, parameters1, context);
        var request2 = new ToolCallRequest(toolName, parameters2, context);

        // Act & Assert
        Assert.NotEqual(request1, request2);
        Assert.False(request1.Equals(request2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentContext()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "key", "value" } };

        var request1 = new ToolCallRequest(toolName, parameters, "Context 1");
        var request2 = new ToolCallRequest(toolName, parameters, "Context 2");

        // Act & Assert
        Assert.NotEqual(request1, request2);
        Assert.False(request1.Equals(request2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityUsingOneWithContextOneWithout()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "key", "value" } };

        var request1 = new ToolCallRequest(toolName, parameters, "With context");
        var request2 = new ToolCallRequest(toolName, parameters);

        // Act & Assert
        Assert.NotEqual(request1, request2);
        Assert.False(request1.Equals(request2));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "key", "value" } };
        var context = "Test context";

        var request1 = new ToolCallRequest(toolName, parameters, context);
        var request2 = new ToolCallRequest(toolName, parameters, context);

        // Act
        var hash1 = request1.GetHashCode();
        var hash2 = request2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { { "key", "value" } };

        var request1 = new ToolCallRequest("Tool1", parameters);
        var request2 = new ToolCallRequest("Tool2", parameters);

        // Act
        var hash1 = request1.GetHashCode();
        var hash2 = request2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Record Deconstruction Tests

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingDeconstruct()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "key", "value" } };
        var context = "Test context";

        var request = new ToolCallRequest(toolName, parameters, context);

        // Act
        var (extractedToolName, extractedParameters, extractedContext) = request;

        // Assert
        Assert.Equal(toolName, extractedToolName);
        Assert.Equal(parameters, extractedParameters);
        Assert.Equal(context, extractedContext);
    }

    [Fact]
    public void ShouldExtractNull_WhenUsingDeconstructWithNullContext()
    {
        // Arrange
        var toolName = "TestTool";
        var parameters = new Dictionary<string, object?> { { "key", "value" } };

        var request = new ToolCallRequest(toolName, parameters);

        // Act
        var (extractedToolName, extractedParameters, extractedContext) = request;

        // Assert
        Assert.Equal(toolName, extractedToolName);
        Assert.Equal(parameters, extractedParameters);
        Assert.Null(extractedContext);
    }

    #endregion

    #region Record With Expression Tests

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedToolName_WhenUsingWithModifyToolName()
    {
        // Arrange
        var originalRequest = new ToolCallRequest(
            "OriginalTool",
            new Dictionary<string, object?> { { "key", "value" } },
            "Original context");

        // Act
        var modifiedRequest = originalRequest with { ToolName = "ModifiedTool" };

        // Assert
        Assert.Equal("OriginalTool", originalRequest.ToolName);
        Assert.Equal("ModifiedTool", modifiedRequest.ToolName);
        Assert.Equal(originalRequest.Parameters, modifiedRequest.Parameters);
        Assert.Equal(originalRequest.Context, modifiedRequest.Context);
        Assert.NotEqual(originalRequest, modifiedRequest);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedParameters_WhenUsingWithModifyParameters()
    {
        // Arrange
        var originalParameters = new Dictionary<string, object?> { { "original", "value" } };
        var newParameters = new Dictionary<string, object?> { { "new", "value" } };

        var originalRequest = new ToolCallRequest("TestTool", originalParameters, "Context");

        // Act
        var modifiedRequest = originalRequest with { Parameters = newParameters };

        // Assert
        Assert.Equal(originalParameters, originalRequest.Parameters);
        Assert.Equal(newParameters, modifiedRequest.Parameters);
        Assert.Equal(originalRequest.ToolName, modifiedRequest.ToolName);
        Assert.Equal(originalRequest.Context, modifiedRequest.Context);
        Assert.NotEqual(originalRequest, modifiedRequest);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedContext_WhenUsingWithModifyContext()
    {
        // Arrange
        var originalRequest = new ToolCallRequest(
            "TestTool",
            new Dictionary<string, object?> { { "key", "value" } },
            "Original context");

        // Act
        var modifiedRequest = originalRequest with { Context = "Modified context" };

        // Assert
        Assert.Equal("Original context", originalRequest.Context);
        Assert.Equal("Modified context", modifiedRequest.Context);
        Assert.Equal(originalRequest.ToolName, modifiedRequest.ToolName);
        Assert.Equal(originalRequest.Parameters, modifiedRequest.Parameters);
        Assert.NotEqual(originalRequest, modifiedRequest);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithContext_WhenUsingWithAddContext()
    {
        // Arrange
        var originalRequest = new ToolCallRequest(
            "TestTool",
            new Dictionary<string, object?> { { "key", "value" } });

        // Act
        var modifiedRequest = originalRequest with { Context = "Added context" };

        // Assert
        Assert.Null(originalRequest.Context);
        Assert.Equal("Added context", modifiedRequest.Context);
        Assert.NotEqual(originalRequest, modifiedRequest);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithoutContext_WhenUsingWithRemoveContext()
    {
        // Arrange
        var originalRequest = new ToolCallRequest(
            "TestTool",
            new Dictionary<string, object?> { { "key", "value" } },
            "Original context");

        // Act
        var modifiedRequest = originalRequest with { Context = null };

        // Assert
        Assert.Equal("Original context", originalRequest.Context);
        Assert.Null(modifiedRequest.Context);
        Assert.NotEqual(originalRequest, modifiedRequest);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithAllUpdates_WhenUsingWithModifyMultipleProperties()
    {
        // Arrange
        var originalRequest = new ToolCallRequest(
            "OriginalTool",
            new Dictionary<string, object?> { { "original", "value" } },
            "Original context");

        var newParameters = new Dictionary<string, object?> { { "new", "value" } };

        // Act
        var modifiedRequest = originalRequest with
        {
            ToolName = "NewTool",
            Parameters = newParameters,
            Context = "New context"
        };

        // Assert
        Assert.Equal("NewTool", modifiedRequest.ToolName);
        Assert.Equal(newParameters, modifiedRequest.Parameters);
        Assert.Equal("New context", modifiedRequest.Context);
        Assert.NotEqual(originalRequest, modifiedRequest);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingToolCallRequestInCollection()
    {
        // Arrange
        var requests = new List<ToolCallRequest>
        {
            new(ToolFileRead, new Dictionary<string, object?> { { ParamPath, "file1.txt" } }),
            new(ToolWebScrape, new Dictionary<string, object?> { { ParamUrl, TestBaseUrl } }),
            new("Calculator", new Dictionary<string, object?> { { "operation", "add" } }),
            new(ToolFileRead, new Dictionary<string, object?> { { ParamPath, "file2.txt" } }),
        };

        // Act
        var fileReadRequests = requests.Where(r => r.ToolName == ToolFileRead).ToList();
        var requestsWithContext = requests.Where(r => r.Context != null).ToList();
        var toolNames = requests.Select(r => r.ToolName).Distinct().ToList();

        // Assert
        Assert.Equal(4, requests.Count);
        Assert.Equal(2, fileReadRequests.Count);
        Assert.Empty(requestsWithContext); // No context in test data
        Assert.Equal(3, toolNames.Count);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingToolCallRequestInHashSet()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { { "key", "value" } };
        var request1 = new ToolCallRequest("TestTool", parameters, "Context");
        var request2 = new ToolCallRequest("TestTool", parameters, "Context"); // Identical
        var request3 = new ToolCallRequest("TestTool", parameters, "Different context");

        var hashSet = new HashSet<ToolCallRequest>
        {
            // Act
            request1,
            request2, // Should be treated as duplicate
            request3
        };

        // Assert
        Assert.Equal(2, hashSet.Count); // request1 and request2 are equal
        Assert.Contains(request1, hashSet);
        Assert.Contains(request3, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingToolCallRequestInDictionary()
    {
        // Arrange
        var request1 = new ToolCallRequest("Tool1", new Dictionary<string, object?> { { "param", "value1" } });
        var request2 = new ToolCallRequest("Tool2", new Dictionary<string, object?> { { "param", "value2" } });

        var dictionary = new Dictionary<ToolCallRequest, string>
        {
            { request1, "Processing" },
            { request2, Completed }
        };

        // Act & Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Processing", dictionary[request1]);
        Assert.Equal(Completed, dictionary[request2]);
        Assert.True(dictionary.ContainsKey(request1));
        Assert.True(dictionary.ContainsKey(request2));
    }

    #endregion

    #region Business Logic and Semantic Tests

    [Fact]
    public void ShouldContainExpectedParameters_WhenUsingToolCallRequestForFileOperations()
    {
        // Arrange & Act
        var fileReadRequest = new ToolCallRequest(ToolFileRead,
            new Dictionary<string, object?>
            {
                { "filePath", "/data/input.txt" },
                { "encoding", "utf-8" }
            },
            "Reading input data for processing");

        var fileWriteRequest = new ToolCallRequest(ToolFileWrite,
            new Dictionary<string, object?>
            {
                { "filePath", "/data/output.txt" },
                { "content", "Processed data" },
                { "append", false }
            });

        // Assert
        Assert.Equal(ToolFileRead, fileReadRequest.ToolName);
        Assert.Contains("filePath", fileReadRequest.Parameters.Keys);
        Assert.Equal("/data/input.txt", fileReadRequest.Parameters["filePath"]);
        Assert.NotNull(fileReadRequest.Context);

        Assert.Equal(ToolFileWrite, fileWriteRequest.ToolName);
        Assert.Contains("content", fileWriteRequest.Parameters.Keys);
        Assert.Null(fileWriteRequest.Context);
    }

    [Fact]
    public void ShouldContainExpectedParameters_WhenUsingToolCallRequestForHttpOperations()
    {
        // Arrange & Act
        var httpRequest = new ToolCallRequest("HttpGet",
            new Dictionary<string, object?>
            {
                { ParamUrl, "https://api.example.com/data" },
                { "headers", new Dictionary<string, string>
                    {
                        { "Authorization", "Bearer token123" },
                        { "Accept", "application/json" }
                    }
                },
                { "timeout", 30000 }
            },
            "Fetching external API data");

        // Assert
        Assert.Equal("HttpGet", httpRequest.ToolName);
        Assert.Contains(ParamUrl, httpRequest.Parameters.Keys);
        Assert.Contains("headers", httpRequest.Parameters.Keys);
        Assert.Contains("timeout", httpRequest.Parameters.Keys);
        Assert.Equal("Fetching external API data", httpRequest.Context);
    }

    [Theory]
    [InlineData("Calculate", new[] { "expression", "precision" })]
    [InlineData("SendEmail", new[] { "to", "subject", "body" })]
    [InlineData(ToolDatabaseQuery, new[] { ParamQuery, "parameters", "timeout" })]
    public void ShouldAcceptExpectedParameterNames_WhenUsingToolCallRequestForCommonTools(string toolName, string[] expectedParams)
    {
        // Arrange
        var parameters = new Dictionary<string, object?>();
        foreach (var param in expectedParams)
        {
            parameters.Add(param, $"value-{param}");
        }

        // Act
        var request = new ToolCallRequest(toolName, parameters);

        // Assert
        Assert.Equal(toolName, request.ToolName);
        Assert.Equal(expectedParams.Length, request.Parameters.Count);
        foreach (var expectedParam in expectedParams)
        {
            Assert.Contains(expectedParam, request.Parameters.Keys);
            Assert.Equal($"value-{expectedParam}", request.Parameters[expectedParam]);
        }
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToolCallRequestWithEmptyStringParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            { "emptyString", "" },
            { "whitespace", "   " },
            { "normalString", "normal" }
        };

        // Act
        var request = new ToolCallRequest("TestTool", parameters);

        // Assert
        Assert.Equal("", request.Parameters["emptyString"]);
        Assert.Equal("   ", request.Parameters["whitespace"]);
        Assert.Equal("normal", request.Parameters["normalString"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToolCallRequestWithSpecialCharactersInToolName()
    {
        // Arrange
        var specialToolName = "Tool-Name_With.Special@Chars#123";
        var parameters = new Dictionary<string, object?> { { "param", "value" } };

        // Act
        var request = new ToolCallRequest(specialToolName, parameters);

        // Assert
        Assert.Equal(specialToolName, request.ToolName);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingToolCallRequestToString()
    {
        // Arrange
        var request = new ToolCallRequest(
            "TestTool",
            new Dictionary<string, object?> { { "param", "value" } },
            "Test context");

        // Act
        var stringRepresentation = request.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("ToolCallRequest", stringRepresentation);
    }

    [Fact]
    public void ShouldMaintainIndependence_WhenUsingToolCallRequestUsingToolChaining()
    {
        // Arrange - Simulate tool chaining where output of one tool becomes input of another
        var step1Request = new ToolCallRequest("DataExtract",
            new Dictionary<string, object?> { { "source", "database" } },
            "Extract raw data");

        var step2Request = new ToolCallRequest("DataTransform",
            new Dictionary<string, object?> { { "inputData", "result_from_step1" } },
            "Transform extracted data");

        var step3Request = new ToolCallRequest("DataLoad",
            new Dictionary<string, object?> { { "transformedData", "result_from_step2" } },
            "Load transformed data");

        // Act - Verify each request is independent
        var allRequests = new[] { step1Request, step2Request, step3Request };

        // Assert
        Assert.Equal(3, allRequests.Length);
        Assert.All(allRequests, r => Assert.NotNull(r.ToolName));
        Assert.All(allRequests, r => Assert.NotEmpty(r.Parameters));
        Assert.All(allRequests, r => Assert.NotNull(r.Context));

        // Verify they are all different
        var uniqueToolNames = allRequests.Select(r => r.ToolName).Distinct().Count();
        Assert.Equal(3, uniqueToolNames);
    }

    #endregion
}
