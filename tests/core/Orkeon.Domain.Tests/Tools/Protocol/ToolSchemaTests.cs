using Orkeon.Domain.Tools.Protocol;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.Tools.Protocol;

/// <summary>
/// Tests for ToolSchema and ParameterSchema following Clean Architecture principles.
/// Tests the tool schema records for tool definition and parameter specification.
/// </summary>
public class ToolSchemaTests
{
    private static readonly string[] RequiredFields = ["rowCount", "columnCount"];
    private static readonly int[] Int123 = [1, 2, 3];
    #region ParameterSchema Tests

    [Fact]
    public void ShouldCreateValidSchema_WhenUsingParameterSchemaWithRequiredParameters()
    {
        // Arrange
        var type = "string";
        var description = "File path to read";
        var required = true;

        // Act
        var paramSchema = new ParameterSchema(type, description, required);

        // Assert
        Assert.Equal(type, paramSchema.Type);
        Assert.Equal(description, paramSchema.Description);
        Assert.True(paramSchema.Required);
        Assert.Null(paramSchema.Default);
        Assert.Null(paramSchema.Enum);
    }

    [Fact]
    public void ShouldCreateValidSchema_WhenUsingParameterSchemaWithAllParameters()
    {
        // Arrange
        var type = "string";
        var description = "Encoding format for file";
        var required = false;
        var defaultValue = "utf-8";
        var enumValues = new List<object> { "utf-8", "ascii", "unicode" };

        // Act
        var paramSchema = new ParameterSchema(type, description, required, defaultValue, enumValues);

        // Assert
        Assert.Equal(type, paramSchema.Type);
        Assert.Equal(description, paramSchema.Description);
        Assert.False(paramSchema.Required);
        Assert.Equal(defaultValue, paramSchema.Default);
        Assert.Equal(enumValues, paramSchema.Enum);
        Assert.Equal(3, paramSchema.Enum!.Count);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("array")]
    [InlineData("object")]
    [InlineData("null")]
    public void ShouldAcceptAll_WhenUsingParameterSchemaWithVariousTypes(string type)
    {
        // Act
        var paramSchema = new ParameterSchema(type, "Test description", true);

        // Assert
        Assert.Equal(type, paramSchema.Type);
        Assert.Equal("Test description", paramSchema.Description);
        Assert.True(paramSchema.Required);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingParameterSchemaWithComplexDefaultValue()
    {
        // Arrange
        var complexDefault = new
        {
            timeout = 30,
            retries = 3,
            headers = new Dictionary<string, string> { { "Accept", "application/json" } }
        };

        // Act
        var paramSchema = new ParameterSchema("object", "HTTP configuration", false, complexDefault);

        // Assert
        Assert.Equal("object", paramSchema.Type);
        Assert.Equal(complexDefault, paramSchema.Default);
        Assert.False(paramSchema.Required);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingParameterSchemaWithVariousEnumTypes()
    {
        // Arrange
        var stringEnum = new List<object> { "GET", "POST", "PUT", "DELETE" };
        var numberEnum = new List<object> { 1, 2, 3, 5, 8, 13 };
        var mixedEnum = new List<object> { "auto", 100, true, null! };

        // Act
        var stringSchema = new ParameterSchema("string", "HTTP method", true, null, stringEnum);
        var numberSchema = new ParameterSchema("number", "Fibonacci number", false, 1, numberEnum);
        var mixedSchema = new ParameterSchema("any", "Mixed values", false, null, mixedEnum);

        // Assert
        Assert.Equal(stringEnum, stringSchema.Enum);
        Assert.Equal(numberEnum, numberSchema.Enum);
        Assert.Equal(mixedEnum, mixedSchema.Enum);
        Assert.Equal(4, stringSchema.Enum!.Count);
        Assert.Equal(6, numberSchema.Enum!.Count);
        Assert.Equal(4, mixedSchema.Enum!.Count);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingParameterSchemaUsingEquality()
    {
        // Arrange
        var enumList = new List<object> { "a", "b" };
        var schema1 = new ParameterSchema("string", "Test param", true, "default", enumList);
        var schema2 = new ParameterSchema("string", "Test param", true, "default", enumList);
        var schema3 = new ParameterSchema("int", "Test param", true, "default", enumList);

        // Act & Assert
        Assert.Equal(schema1, schema2);
        Assert.NotEqual(schema1, schema3);
        Assert.True(schema1 == schema2);
        Assert.False(schema1 == schema3);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingParameterSchemaWithExpression()
    {
        // Arrange
        var original = new ParameterSchema("string", "Original description", true, null, null);

        // Act
        var modified = original with { Required = false, Default = "new default" };

        // Assert
        Assert.True(original.Required);
        Assert.Null(original.Default);
        Assert.False(modified.Required);
        Assert.Equal("new default", modified.Default);
        Assert.NotEqual(original, modified);
    }

    #endregion

    #region ToolSchema Tests

    [Fact]
    public void ShouldCreateValidSchema_WhenUsingToolSchemaWithRequiredParameters()
    {
        // Arrange
        var name = ToolFileRead;
        var description = "Reads content from a file";
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "filePath", new ParameterSchema("string", "Path to the file", true) },
            { "encoding", new ParameterSchema("string", "File encoding", false, "utf-8") }
        };

        // Act
        var toolSchema = new ToolSchema(name, description, parameters);

        // Assert
        Assert.Equal(name, toolSchema.Name);
        Assert.Equal(description, toolSchema.Description);
        Assert.Equal(parameters, toolSchema.Parameters);
        Assert.Null(toolSchema.Returns);
    }

    [Fact]
    public void ShouldCreateValidSchema_WhenUsingToolSchemaWithAllParameters()
    {
        // Arrange
        var name = "Calculator";
        var description = "Performs mathematical calculations";
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "operation", new ParameterSchema("string", "Math operation", true, null,
                ["add", "subtract", "multiply", "divide"]) },
            { "operands", new ParameterSchema("array", "Numbers to operate on", true) }
        };
        var returns = new Dictionary<string, object?>
        {
            { "type", "number" },
            { "description", "Result of the calculation" }
        };

        // Act
        var toolSchema = new ToolSchema(name, description, parameters, returns);

        // Assert
        Assert.Equal(name, toolSchema.Name);
        Assert.Equal(description, toolSchema.Description);
        Assert.Equal(parameters, toolSchema.Parameters);
        Assert.Equal(returns, toolSchema.Returns);
        Assert.Equal(2, toolSchema.Parameters!.Count);
        Assert.Equal(2, toolSchema.Returns!.Count);
    }

    [Fact]
    public void ShouldAcceptEmptyDictionary_WhenUsingToolSchemaWithEmptyParameters()
    {
        // Arrange
        var name = "CurrentTime";
        var description = "Gets the current system time";
        var emptyParameters = new Dictionary<string, ParameterSchema>();

        // Act
        var toolSchema = new ToolSchema(name, description, emptyParameters);

        // Assert
        Assert.Equal(name, toolSchema.Name);
        Assert.Equal(description, toolSchema.Description);
        Assert.Equal(emptyParameters, toolSchema.Parameters);
        Assert.NotNull(toolSchema.Parameters);
        Assert.Empty(toolSchema.Parameters);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToolSchemaWithComplexParameters()
    {
        // Arrange
        var name = "HttpRequest";
        var description = "Makes HTTP requests to external APIs";
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { ParamUrl, new ParameterSchema("string", "Target URL", true) },
            { "method", new ParameterSchema("string", "HTTP method", false, "GET",
                ["GET", "POST", "PUT", "DELETE", "PATCH"]) },
            { "headers", new ParameterSchema("object", "HTTP headers", false,
                new Dictionary<string, string> { { "Accept", "application/json" } }) },
            { "timeout", new ParameterSchema("number", "Request timeout in milliseconds", false, 30000) },
            { "retries", new ParameterSchema("integer", "Number of retry attempts", false, 0) },
            { "validateCertificate", new ParameterSchema("boolean", "Validate SSL certificate", false, true) }
        };

        // Act
        var toolSchema = new ToolSchema(name, description, parameters);

        // Assert
        Assert.Equal(6, toolSchema.Parameters.Count);
        Assert.True(toolSchema.Parameters[ParamUrl].Required);
        Assert.False(toolSchema.Parameters["method"].Required);
        Assert.Equal("GET", toolSchema.Parameters["method"].Default);
        Assert.Equal(5, toolSchema.Parameters["method"].Enum!.Count);
        Assert.Equal(30000, toolSchema.Parameters["timeout"].Default);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToolSchemaWithComplexReturnSchema()
    {
        // Arrange
        var name = "DataAnalysis";
        var description = "Analyzes dataset and returns statistical information";
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "dataset", new ParameterSchema("string", "Path to dataset file", true) }
        };
        var returns = new Dictionary<string, object?>
        {
            { "type", "object" },
            { "description", "Analysis results" },
            { "properties", new Dictionary<string, object?>
                {
                    { "rowCount", new { type = "integer", description = "Number of rows" } },
                    { "columnCount", new { type = "integer", description = "Number of columns" } },
                    { "statistics", new { type = "object", description = "Statistical summary" } },
                    { "errors", new { type = "array", description = "Validation errors found" } }
                }
            },
            { "required", RequiredFields }
        };

        // Act
        var toolSchema = new ToolSchema(name, description, parameters, returns);

        // Assert
        Assert.Equal("DataAnalysis", toolSchema.Name);
        Assert.Equal(4, toolSchema.Returns!.Count);
        Assert.Equal("object", toolSchema.Returns["type"]);
        Assert.IsType<Dictionary<string, object?>>(toolSchema.Returns["properties"]);
        Assert.IsType<string[]>(toolSchema.Returns["required"]);
    }

    #endregion

    #region ToolSchema Equality and HashCode Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingToolSchemaUsingEqualitySameValues()
    {
        // Arrange
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "param1", new ParameterSchema("string", "Test param", true) }
        };
        var returns = new Dictionary<string, object?> { { "type", "string" } };

        var schema1 = new ToolSchema("TestTool", "Test description", parameters, returns);
        var schema2 = new ToolSchema("TestTool", "Test description", parameters, returns);

        // Act & Assert
        Assert.Equal(schema1, schema2);
        Assert.True(schema1.Equals(schema2));
        Assert.True(schema1 == schema2);
        Assert.False(schema1 != schema2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingToolSchemaUsingEqualityDifferentName()
    {
        // Arrange
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "param1", new ParameterSchema("string", "Test param", true) }
        };

        var schema1 = new ToolSchema("Tool1", "Description", parameters);
        var schema2 = new ToolSchema("Tool2", "Description", parameters);

        // Act & Assert
        Assert.NotEqual(schema1, schema2);
        Assert.False(schema1.Equals(schema2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingToolSchemaUsingEqualityDifferentParameters()
    {
        // Arrange
        var parameters1 = new Dictionary<string, ParameterSchema>
        {
            { "param1", new ParameterSchema("string", "Test param", true) }
        };
        var parameters2 = new Dictionary<string, ParameterSchema>
        {
            { "param2", new ParameterSchema("int", "Different param", false) }
        };

        var schema1 = new ToolSchema("TestTool", "Description", parameters1);
        var schema2 = new ToolSchema("TestTool", "Description", parameters2);

        // Act & Assert
        Assert.NotEqual(schema1, schema2);
        Assert.False(schema1.Equals(schema2));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenUsingToolSchemaGettingHashCodeSameValues()
    {
        // Arrange
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "param1", new ParameterSchema("string", "Test param", true) }
        };

        var schema1 = new ToolSchema("TestTool", "Description", parameters);
        var schema2 = new ToolSchema("TestTool", "Description", parameters);

        // Act
        var hash1 = schema1.GetHashCode();
        var hash2 = schema2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Record Deconstruction Tests

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingToolSchemaUsingDeconstruct()
    {
        // Arrange
        var name = "TestTool";
        var description = "Test description";
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "param1", new ParameterSchema("string", "Test param", true) }
        };
        var returns = new Dictionary<string, object?> { { "type", "object" } };

        var toolSchema = new ToolSchema(name, description, parameters, returns);

        // Act
        var (extractedName, extractedDescription, extractedParameters, extractedReturns, _) = toolSchema;

        // Assert
        Assert.Equal(name, extractedName);
        Assert.Equal(description, extractedDescription);
        Assert.Equal(parameters, extractedParameters);
        Assert.Equal(returns, extractedReturns);
    }

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingParameterSchemaUsingDeconstruct()
    {
        // Arrange
        var type = "string";
        var description = "Test parameter";
        var required = true;
        var defaultValue = "default";
        var enumValues = new List<object> { "a", "b", "c" };

        var paramSchema = new ParameterSchema(type, description, required, defaultValue, enumValues);

        // Act
        var (extractedType, extractedDescription, extractedRequired, extractedDefault, extractedEnum, extractedFormat, extractedItemsType, extractedItemsFormat, _, _) = paramSchema;

        // Assert
        Assert.Equal(type, extractedType);
        Assert.Equal(description, extractedDescription);
        Assert.Equal(required, extractedRequired);
        Assert.Equal(defaultValue, extractedDefault);
        Assert.Equal(enumValues, extractedEnum);
        Assert.Null(extractedFormat);
        Assert.Null(extractedItemsType);
        Assert.Null(extractedItemsFormat);
    }

    #endregion

    #region Record With Expression Tests

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingToolSchemaWithExpressionModifyName()
    {
        // Arrange
        var original = new ToolSchema("OriginalTool", "Description",
            []);

        // Act
        var modified = original with { Name = "ModifiedTool" };

        // Assert
        Assert.Equal("OriginalTool", original.Name);
        Assert.Equal("ModifiedTool", modified.Name);
        Assert.Equal(original.Description, modified.Description);
        Assert.NotEqual(original, modified);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingToolSchemaWithExpressionModifyParameters()
    {
        // Arrange
        var originalParams = new Dictionary<string, ParameterSchema>
        {
            { "original", new ParameterSchema("string", "Original param", true) }
        };
        var newParams = new Dictionary<string, ParameterSchema>
        {
            { "new", new ParameterSchema("int", "New param", false) }
        };

        var original = new ToolSchema("TestTool", "Description", originalParams);

        // Act
        var modified = original with { Parameters = newParams };

        // Assert
        Assert.Equal(originalParams, original.Parameters);
        Assert.Equal(newParams, modified.Parameters);
        Assert.NotEqual(original, modified);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingToolSchemaWithExpressionAddReturns()
    {
        // Arrange
        var original = new ToolSchema("TestTool", "Description",
            []);
        var returns = new Dictionary<string, object?> { { "type", "string" } };

        // Act
        var modified = original with { Returns = returns };

        // Assert
        Assert.Null(original.Returns);
        Assert.Equal(returns, modified.Returns);
        Assert.NotEqual(original, modified);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingParameterSchemaInCollection()
    {
        // Arrange
        var parameters = new List<ParameterSchema>
        {
            new("string", "String parameter", true),
            new("int", "Integer parameter", false, 0),
            new("boolean", "Boolean parameter", true),
            new("string", "Another string parameter", false)
        };

        // Act
        var requiredParams = parameters.Where(p => p.Required).ToList();
        var stringParams = parameters.Where(p => p.Type == "string").ToList();
        var paramsWithDefaults = parameters.Where(p => p.Default != null).ToList();

        // Assert
        Assert.Equal(4, parameters.Count);
        Assert.Equal(2, requiredParams.Count);
        Assert.Equal(2, stringParams.Count);
        Assert.Single(paramsWithDefaults);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingToolSchemaInDictionary()
    {
        // Arrange
        var schema1 = new ToolSchema("Tool1", "Description 1",
            []);
        var schema2 = new ToolSchema("Tool2", "Description 2",
            []);

        var toolRegistry = new Dictionary<ToolSchema, string>
        {
            { schema1, "v1.0.0" },
            { schema2, "v2.1.0" }
        };

        // Act & Assert
        Assert.Equal(2, toolRegistry.Count);
        Assert.Equal("v1.0.0", toolRegistry[schema1]);
        Assert.Equal("v2.1.0", toolRegistry[schema2]);
        Assert.True(toolRegistry.ContainsKey(schema1));
        Assert.True(toolRegistry.ContainsKey(schema2));
    }

    #endregion

    #region Business Logic and Semantic Tests

    [Theory]
    [InlineData(ToolFileRead, new[] { "filePath" }, new[] { "encoding" })]
    [InlineData("HttpGet", new[] { "url" }, new[] { "headers", "timeout" })]
    [InlineData(ToolDatabaseQuery, new[] { "query" }, new[] { "parameters", "timeout" })]
    public void ShouldHaveExpectedParameterStructure_WhenUsingToolSchemaForCommonTools(
        string toolName, string[] requiredParams, string[] optionalParams)
    {
        // Arrange
        var parameters = new Dictionary<string, ParameterSchema>();

        foreach (var param in requiredParams)
        {
            parameters.Add(param, new ParameterSchema("string", $"{param} parameter", true));
        }

        foreach (var param in optionalParams)
        {
            parameters.Add(param, new ParameterSchema("string", $"{param} parameter", false));
        }

        // Act
        var toolSchema = new ToolSchema(toolName, $"{toolName} tool", parameters);

        // Assert
        Assert.Equal(toolName, toolSchema.Name);
        Assert.Equal(requiredParams.Length + optionalParams.Length, toolSchema.Parameters.Count);

        foreach (var requiredParam in requiredParams)
        {
            Assert.True(toolSchema.Parameters[requiredParam].Required);
        }

        foreach (var optionalParam in optionalParams)
        {
            Assert.False(toolSchema.Parameters[optionalParam].Required);
        }
    }

    [Fact]
    public void ShouldContainExpectedStructure_WhenUsingToolSchemaForWebScrapingTool()
    {
        // Arrange & Act
        var webScrapingSchema = new ToolSchema(
            ToolWebScrape,
            "Extracts data from web pages using CSS selectors",
            new Dictionary<string, ParameterSchema>
            {
                { ParamUrl, new ParameterSchema("string", "Target URL to scrape", true) },
                { "selector", new ParameterSchema("string", "CSS selector for target elements", true) },
                { "attribute", new ParameterSchema("string", "HTML attribute to extract", false, "text") },
                { "timeout", new ParameterSchema("number", "Request timeout in seconds", false, 30) },
                { "userAgent", new ParameterSchema("string", "Custom user agent string", false,
                    "Orkeon-WebScraper/1.0") }
            },
            new Dictionary<string, object?>
            {
                { "type", "array" },
                { "description", "Extracted data from matched elements" },
                { "items", new { type = "string" } }
            });

        // Assert
        Assert.Equal(ToolWebScrape, webScrapingSchema.Name);
        Assert.Contains("CSS selector", webScrapingSchema.Description);
        Assert.Equal(5, webScrapingSchema.Parameters.Count);
        Assert.True(webScrapingSchema.Parameters[ParamUrl].Required);
        Assert.True(webScrapingSchema.Parameters["selector"].Required);
        Assert.False(webScrapingSchema.Parameters["timeout"].Required);
        Assert.Equal(30, webScrapingSchema.Parameters["timeout"].Default);
        Assert.Equal("array", webScrapingSchema.Returns!["type"]);
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenUsingParameterSchemaForEnumParameter()
    {
        // Arrange
        var httpMethodSchema = new ParameterSchema(
            "string",
            "HTTP method for the request",
            true,
            null,
            ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"]);

        var logLevelSchema = new ParameterSchema(
            "string",
            "Logging level",
            false,
            "INFO",
            ["DEBUG", "INFO", "WARN", "ERROR", "FATAL"]);

        // Act & Assert
        Assert.Equal("string", httpMethodSchema.Type);
        Assert.True(httpMethodSchema.Required);
        Assert.Null(httpMethodSchema.Default);
        Assert.Equal(7, httpMethodSchema.Enum!.Count);
        Assert.Contains("GET", httpMethodSchema.Enum);
        Assert.Contains("POST", httpMethodSchema.Enum);

        Assert.Equal("string", logLevelSchema.Type);
        Assert.False(logLevelSchema.Required);
        Assert.Equal("INFO", logLevelSchema.Default);
        Assert.Equal(5, logLevelSchema.Enum!.Count);
        Assert.Contains("DEBUG", logLevelSchema.Enum);
        Assert.Contains("FATAL", logLevelSchema.Enum);
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingToolSchemaWithVeryLongDescription()
    {
        // Arrange
        var longDescription = new string('A', 1000) + " - This is a very detailed description of what this tool does.";

        // Act
        var toolSchema = new ToolSchema("TestTool", longDescription,
            []);

        // Assert
        Assert.Equal(longDescription, toolSchema.Description);
        Assert.True(toolSchema.Description.Length > 1000);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingParameterSchemaWithComplexEnumValues()
    {
        // Arrange
        var complexEnum = new List<object>
        {
            "simple_string",
            42,
            true,
            new { type = "object", value = 123 },
            Int123,
            null!
        };

        // Act
        var paramSchema = new ParameterSchema("any", "Parameter with complex enum", false, null, complexEnum);

        // Assert
        Assert.Equal(6, paramSchema.Enum!.Count);
        Assert.Contains("simple_string", paramSchema.Enum);
        Assert.Contains(42, paramSchema.Enum);
        Assert.Contains(true, paramSchema.Enum);
        Assert.Contains(null, paramSchema.Enum);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingToolSchemaToString()
    {
        // Arrange
        var toolSchema = new ToolSchema("TestTool", "Test description",
            new Dictionary<string, ParameterSchema>
            {
                { "param1", new ParameterSchema("string", "Test param", true) }
            });

        // Act
        var stringRepresentation = toolSchema.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("ToolSchema", stringRepresentation);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingParameterSchemaToString()
    {
        // Arrange
        var paramSchema = new ParameterSchema("string", "Test parameter", true, "default",
            ["a", "b"]);

        // Act
        var stringRepresentation = paramSchema.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("ParameterSchema", stringRepresentation);
    }

    [Fact]
    public void ShouldAffectOriginalDictionary_WhenUsingToolSchemaUsingParameterModificationAfterCreation()
    {
        // Arrange
        var parameters = new Dictionary<string, ParameterSchema>
        {
            { "initial", new ParameterSchema("string", "Initial param", true) }
        };
        var toolSchema = new ToolSchema("TestTool", "Description", parameters);

        // Act
        parameters.Add("added", new ParameterSchema("int", "Added param", false));

        // Assert
        Assert.Equal(2, toolSchema.Parameters.Count);
        Assert.Contains("added", toolSchema.Parameters.Keys);
        Assert.Equal("int", toolSchema.Parameters["added"].Type);
    }

    [Fact]
    public void ShouldSupportVersioning_WhenUsingToolSchemaUsingSchemaEvolution()
    {
        // Arrange - Simulate schema evolution
        var v1Schema = new ToolSchema(
            "ApiClient",
            "Makes API calls (v1)",
            new Dictionary<string, ParameterSchema>
            {
                { ParamUrl, new ParameterSchema("string", "API endpoint", true) },
                { "method", new ParameterSchema("string", "HTTP method", false, "GET") }
            },
            new Dictionary<string, object?> { { "version", "1.0" } });

        var v2Schema = v1Schema with
        {
            Description = "Makes API calls with advanced features (v2)",
            Parameters = new Dictionary<string, ParameterSchema>(v1Schema.Parameters)
            {
                { "headers", new ParameterSchema("object", "Custom headers", false) },
                { "timeout", new ParameterSchema("number", "Request timeout", false, 30) }
            },
            Returns = new Dictionary<string, object?> { { "version", "2.0" } }
        };

        // Act & Assert
        Assert.Equal("1.0", v1Schema.Returns!["version"]);
        Assert.Equal("2.0", v2Schema.Returns!["version"]);
        Assert.Equal(2, v1Schema.Parameters.Count);
        Assert.Equal(4, v2Schema.Parameters.Count);
        Assert.Contains("headers", v2Schema.Parameters.Keys);
        Assert.Contains("timeout", v2Schema.Parameters.Keys);
        Assert.NotEqual(v1Schema, v2Schema);
    }

    #endregion
}
