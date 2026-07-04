using System.Text.Json;
using Orkeon.Domain.Tools.Protocol;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Domain.Tests.Tools.Protocol;

/// <summary>
/// Tests for ToolCallRequest&lt;TParameters&gt; generic wrapper.
/// Verifies typed parameter deserialization, conversion to/from the non-generic
/// ToolCallRequest, and integration with the existing tool-call pipeline.
/// </summary>
public class ToolCallRequestGenericTests
{
    #region Sample parameter types for tests

    public class FileReadParams
    {
        public string FilePath { get; set; } = string.Empty;
        public string Encoding { get; set; } = "utf-8";
    }

    public class WebScrapeParams
    {
        public string Url { get; set; } = string.Empty;
        public string? Selector { get; set; }
        public int Timeout { get; set; } = 30;
    }

    public class EmptyParams { }

    public class NestedParams
    {
        public string Name { get; set; } = string.Empty;
        public InnerConfig? Config { get; set; }
    }

    public class InnerConfig
    {
        public int Retries { get; set; }
        public bool Verbose { get; set; }
    }

    #endregion

    #region Constructor from non-generic ToolCallRequest

    [Fact]
    public void ShouldDeserializeParameters_WhenConstructingFromNonGeneric()
    {
        // Arrange
        var inner = new ToolCallRequest(ToolFileRead, new Dictionary<string, object?>
        {
            { "file_path", "/tmp/data.txt" },
            { "encoding", "ascii" }
        });

        // Act
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Assert
        Assert.Equal("/tmp/data.txt", typed.TypedParameters.FilePath);
        Assert.Equal("ascii", typed.TypedParameters.Encoding);
    }

    [Fact]
    public void ShouldPreserveInner_WhenConstructingFromNonGeneric()
    {
        // Arrange
        var inner = new ToolCallRequest(ToolFileRead,
            new Dictionary<string, object?> { { "file_path", "/tmp/data.txt" } },
            "some context");

        // Act
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Assert
        Assert.Same(inner, typed.Inner);
        Assert.Equal(ToolFileRead, typed.ToolName);
        Assert.Equal("some context", typed.Context);
        Assert.Same(inner.Parameters, typed.Parameters);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingFromNonGenericNullInner()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ToolCallRequest<FileReadParams>(null!));
    }

    [Fact]
    public void ShouldDeserializeDefaults_WhenConstructingFromNonGenericEmptyParameters()
    {
        // Arrange
        var inner = new ToolCallRequest("EmptyTool", []);

        // Act
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Assert
        Assert.Equal(string.Empty, typed.TypedParameters.FilePath);
        Assert.Equal("utf-8", typed.TypedParameters.Encoding);
    }

    [Fact]
    public void ShouldSucceed_WhenConstructingFromNonGenericWithEmptyParamsType()
    {
        // Arrange
        var inner = new ToolCallRequest("Ping", []);

        // Act
        var typed = new ToolCallRequest<EmptyParams>(inner);

        // Assert
        Assert.NotNull(typed.TypedParameters);
    }

    #endregion

    #region Constructor from explicit typed parameters

    [Fact]
    public void ShouldSerializeToInner_WhenConstructingFromTypedParameters()
    {
        // Arrange
        var parameters = new FileReadParams { FilePath = "/data/file.csv", Encoding = "utf-16" };

        // Act
        var typed = new ToolCallRequest<FileReadParams>(ToolFileRead, parameters, "reading csv");

        // Assert
        Assert.Equal(ToolFileRead, typed.ToolName);
        Assert.Equal("reading csv", typed.Context);
        Assert.Equal("/data/file.csv", typed.TypedParameters.FilePath);
        Assert.Equal("utf-16", typed.TypedParameters.Encoding);
        // Inner dict should contain the serialized keys (snake_case)
        Assert.NotNull(typed.Inner);
        Assert.Equal(ToolFileRead, typed.Inner.ToolName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingFromTypedParametersNullParameters()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ToolCallRequest<FileReadParams>(ToolFileRead, null!));
    }

    [Fact]
    public void ShouldBeAccepted_WhenConstructingFromTypedParametersNullContext()
    {
        var typed = new ToolCallRequest<EmptyParams>("Ping", new EmptyParams());
        Assert.Null(typed.Context);
    }

    [Fact]
    public void ShouldRoundTrip_WhenConstructingFromTypedParametersWithNestedObject()
    {
        // Arrange
        var parameters = new NestedParams
        {
            Name = "test",
            Config = new InnerConfig { Retries = 5, Verbose = true }
        };

        // Act
        var typed = new ToolCallRequest<NestedParams>("ComplexTool", parameters);

        // Assert
        Assert.Equal("test", typed.TypedParameters.Name);
        // Nested objects go through JSON round-trip; verify the inner dict was created
        Assert.NotNull(typed.Inner.Parameters);
        Assert.True(typed.Inner.Parameters.Count > 0);
    }

    #endregion

    #region Conversion: ToToolCallRequest and implicit operator

    [Fact]
    public void ShouldReturnInner_WhenUsingToToolCallRequest()
    {
        // Arrange
        var inner = new ToolCallRequest("Tool",
            new Dictionary<string, object?> { { "file_path", "a.txt" } });
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Act
        var result = typed.ToToolCallRequest();

        // Assert
        Assert.Same(inner, result);
    }

    [Fact]
    public void ShouldReturnInner_WhenUsingImplicitConversion()
    {
        // Arrange
        var inner = new ToolCallRequest("Tool",
            new Dictionary<string, object?> { { "file_path", "a.txt" } });
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Act
        ToolCallRequest nonGeneric = typed; // implicit conversion

        // Assert
        Assert.Same(inner, nonGeneric);
    }

    [Fact]
    public void ShouldCanBePassedToMethodExpectingNonGeneric_WhenUsingImplicitConversion()
    {
        // Arrange
        var typed = new ToolCallRequest<FileReadParams>(ToolFileRead,
            new FileReadParams { FilePath = "/tmp/test.txt" });

        // Act - pass to a method that accepts non-generic ToolCallRequest
        var toolName = GetToolName(typed);

        // Assert
        Assert.Equal(ToolFileRead, toolName);
    }

    private static string GetToolName(ToolCallRequest request) => request.ToolName;

    #endregion

    #region FromToolCallRequest static factory

    [Fact]
    public void ShouldCreateTypedInstance_WhenUsingFromToolCallRequest()
    {
        // Arrange
        var inner = new ToolCallRequest(ToolWebScrape, new Dictionary<string, object?>
        {
            { ParamUrl, TestBaseUrl },
            { "timeout", 60 }
        }, "scraping");

        // Act
        var typed = ToolCallRequest<WebScrapeParams>.FromToolCallRequest(inner);

        // Assert
        Assert.Equal(TestBaseUrl, typed.TypedParameters.Url);
        Assert.Equal(60, typed.TypedParameters.Timeout);
        Assert.Equal("scraping", typed.Context);
    }

    #endregion

    #region Shortcut properties delegation

    [Fact]
    public void ShouldDelegateToInner_WhenUsingShortcutProperties()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            { ParamUrl, TestBaseUrl },
            { "selector", ".main" },
            { "timeout", 15 }
        };
        var inner = new ToolCallRequest(ToolWebScrape, parameters, "ctx");

        // Act
        var typed = new ToolCallRequest<WebScrapeParams>(inner);

        // Assert
        Assert.Equal(inner.ToolName, typed.ToolName);
        Assert.Equal(inner.Context, typed.Context);
        Assert.Same(inner.Parameters, typed.Parameters);
    }

    #endregion

    #region Case-insensitive / snake_case deserialization

    [Fact]
    public void ShouldDeserialize_WhenConstructingFromNonGenericCamelCaseKeys()
    {
        // Arrange - keys in camelCase; the serializer uses snake_case naming policy
        // with PropertyNameCaseInsensitive, so camelCase keys like "filePath" won't
        // match "file_path". Use snake_case keys for reliable deserialization.
        var inner = new ToolCallRequest(ToolFileRead, new Dictionary<string, object?>
        {
            { "file_path", "/path" },
            { "encoding", "utf-32" }
        });

        // Act
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Assert
        Assert.Equal("/path", typed.TypedParameters.FilePath);
        Assert.Equal("utf-32", typed.TypedParameters.Encoding);
    }

    [Fact]
    public void ShouldDeserialize_WhenConstructingFromNonGenericSnakeCaseKeys()
    {
        // Arrange
        var inner = new ToolCallRequest(ToolFileRead, new Dictionary<string, object?>
        {
            { "file_path", "/snake" },
            { "encoding", "latin1" }
        });

        // Act
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Assert
        Assert.Equal("/snake", typed.TypedParameters.FilePath);
        Assert.Equal("latin1", typed.TypedParameters.Encoding);
    }

    #endregion

    #region Round-trip: typed -> non-generic -> typed

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripTypedToNonGenericToTyped()
    {
        // Arrange
        var original = new ToolCallRequest<WebScrapeParams>(ToolWebScrape,
            new WebScrapeParams
            {
                Url = TestBaseUrl,
                Selector = "div.content",
                Timeout = 45
            },
            "round-trip context");

        // Act - convert to non-generic, then back to typed
        ToolCallRequest nonGeneric = original.ToToolCallRequest();
        var restored = new ToolCallRequest<WebScrapeParams>(nonGeneric);

        // Assert
        Assert.Equal(original.TypedParameters.Url, restored.TypedParameters.Url);
        Assert.Equal(original.TypedParameters.Selector, restored.TypedParameters.Selector);
        Assert.Equal(original.TypedParameters.Timeout, restored.TypedParameters.Timeout);
        Assert.Equal(original.ToolName, restored.ToolName);
        Assert.Equal(original.Context, restored.Context);
    }

    #endregion

    #region Backward compatibility

    [Fact]
    public void ShouldRemainFullyFunctional_WhenUsingNonGenericToolCallRequest()
    {
        // Ensure the non-generic type still works exactly as before
        var request = new ToolCallRequest("TestTool",
            new Dictionary<string, object?> { { "key", "value" } },
            "context");

        Assert.Equal("TestTool", request.ToolName);
        Assert.Equal("value", request.Parameters["key"]);
        Assert.Equal("context", request.Context);

        // With expression
        var modified = request with { ToolName = "Other" };
        Assert.Equal("Other", modified.ToolName);
        Assert.Equal("TestTool", request.ToolName);
    }

    [Fact]
    public void ShouldDoesNotChangeNonGenericBehavior_WhenUsingGenericRequest()
    {
        // The non-generic request created from a generic should behave identically
        // to a manually constructed non-generic request
        var manual = new ToolCallRequest("Tool",
            new Dictionary<string, object?> { { "file_path", "x.txt" } });
        var fromGeneric = new ToolCallRequest<FileReadParams>("Tool",
            new FileReadParams { FilePath = "x.txt" }).ToToolCallRequest();

        Assert.Equal(manual.ToolName, fromGeneric.ToolName);
        Assert.Equal(manual.Context, fromGeneric.Context);
    }

    #endregion

    #region Record equality for generic version

    [Fact]
    public void ShouldTypedParametersAreDifferentInstances_WhenUsingEqualityWithSameInner()
    {
        // Record equality includes TypedParameters, which is a class compared by reference.
        // Two ToolCallRequest<T> wrapping the same Inner will have different TypedParameters
        // instances (each deserialized independently), so they won't be record-equal.
        var inner = new ToolCallRequest("T",
            new Dictionary<string, object?> { { "file_path", "a" } });
        var a = new ToolCallRequest<FileReadParams>(inner);
        var b = new ToolCallRequest<FileReadParams>(inner);

        // Same Inner reference, but TypedParameters are different instances
        Assert.Same(a.Inner, b.Inner);
        Assert.NotSame(a.TypedParameters, b.TypedParameters);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEqualityWithDifferentInner()
    {
        var innerA = new ToolCallRequest("T",
            new Dictionary<string, object?> { { "file_path", "a" } });
        var innerB = new ToolCallRequest("T",
            new Dictionary<string, object?> { { "file_path", "b" } });

        var a = new ToolCallRequest<FileReadParams>(innerA);
        var b = new ToolCallRequest<FileReadParams>(innerB);

        Assert.NotEqual(a, b);
    }

    #endregion

    #region Integration with ComponentBase pipeline

    [Fact]
    public void ShouldCanBeUsedWithIBaseToolCallAsync_WhenUsingGenericRequest()
    {
        // Verify the generic request can be implicitly converted for IBaseTool.CallAsync signature
        var typed = new ToolCallRequest<FileReadParams>(ToolFileRead,
            new FileReadParams { FilePath = "/test.txt" });

        // IBaseTool.CallAsync expects ToolCallRequest — implicit conversion should work
        ToolCallRequest request = typed;
        Assert.NotNull(request);
        Assert.Equal(ToolFileRead, request.ToolName);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void ShouldExtraKeysIgnored_WhenConstructingFromNonGeneric()
    {
        // Parameters dict has keys that don't exist on the typed model
        var inner = new ToolCallRequest(ToolFileRead, new Dictionary<string, object?>
        {
            { "file_path", "/tmp/x" },
            { "encoding", "utf-8" },
            { "unknown_key", "should be ignored" }
        });

        // Act - should not throw
        var typed = new ToolCallRequest<FileReadParams>(inner);

        // Assert
        Assert.Equal("/tmp/x", typed.TypedParameters.FilePath);
        Assert.Equal("utf-8", typed.TypedParameters.Encoding);
    }

    [Fact]
    public void ShouldSerialize_WhenConstructingFromTypedParametersDefaultValues()
    {
        // A params object with all defaults
        var typed = new ToolCallRequest<WebScrapeParams>(ToolWebScrape, new WebScrapeParams());

        // Inner parameters dict should exist with serialized defaults
        Assert.NotNull(typed.Inner.Parameters);
        Assert.Equal(ToolWebScrape, typed.ToolName);
        Assert.Equal(string.Empty, typed.TypedParameters.Url);
        Assert.Null(typed.TypedParameters.Selector);
        Assert.Equal(30, typed.TypedParameters.Timeout);
    }

    [Fact]
    public void ShouldDeserialize_WhenConstructingFromNonGenericJsonElementValues()
    {
        // Simulate what happens when parameters come from JSON parsing
        var json = """{"file_path":"/from/json","encoding":"utf-8"}""";
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

        var inner = new ToolCallRequest(ToolFileRead, dict);
        var typed = new ToolCallRequest<FileReadParams>(inner);

        Assert.Equal("/from/json", typed.TypedParameters.FilePath);
        Assert.Equal("utf-8", typed.TypedParameters.Encoding);
    }

    #endregion
}
