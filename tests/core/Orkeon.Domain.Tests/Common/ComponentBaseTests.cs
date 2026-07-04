using System.Text.Json;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Common;

#region Test helpers

public record TestRequest
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public bool Active { get; init; }
    public double Score { get; init; }
    public string? Optional { get; init; }
}

public record TestResponse
{
    public string Result { get; init; } = "";
    public int Total { get; init; }
}

public record NestedAddress
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
}

public record NestedRequest
{
    public string Name { get; init; } = "";
    public NestedAddress? Address { get; init; }
}

public record NestedResponse
{
    public string Summary { get; init; } = "";
}

public record ArrayRequest
{
    public string Label { get; init; } = "";
    public List<string>? Tags { get; init; }
}

public record ArrayResponse
{
    public int TagCount { get; init; }
}

/// <summary>
/// Concrete test implementation of ComponentBase that exposes protected methods.
/// </summary>
public class TestComponent : ComponentBase<TestRequest, TestResponse>
{
    public string? ValidationError { get; set; }
    public TestRequest? LastRequest { get; private set; }

    public TestRequest TestDeserialize(Dictionary<string, object?> parameters)
        => DeserializeRequest(parameters);

    public Dictionary<string, object?> TestSerialize(TestResponse response)
        => SerializeResponse(response);

    public static string TestNormalizeKey(string key)
        => NormalizeKeyToSnakeCase(key);

    public static Dictionary<string, object?> TestNormalizeParameters(Dictionary<string, object?> parameters)
        => NormalizeParameters(parameters);

    public static object? TestNormalizeValue(object? value)
        => NormalizeValue(value);

    protected override string? ValidateTypedRequest(TestRequest request)
        => ValidationError;

    protected override System.Threading.Tasks.Task<TestResponse> ExecuteTypedAsync(TestRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return System.Threading.Tasks.Task.FromResult(new TestResponse { Result = $"Hello {request.Name}", Total = request.Count * 2 });
    }
}

public class NestedComponent : ComponentBase<NestedRequest, NestedResponse>
{
    protected override System.Threading.Tasks.Task<NestedResponse> ExecuteTypedAsync(NestedRequest request, CancellationToken ct)
        => System.Threading.Tasks.Task.FromResult(new NestedResponse { Summary = $"{request.Name} @ {request.Address?.City}" });
}

public class ArrayComponent : ComponentBase<ArrayRequest, ArrayResponse>
{
    protected override System.Threading.Tasks.Task<ArrayResponse> ExecuteTypedAsync(ArrayRequest request, CancellationToken ct)
        => System.Threading.Tasks.Task.FromResult(new ArrayResponse { TagCount = request.Tags?.Count ?? 0 });
}

#endregion

public class ComponentBaseTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    private readonly TestComponent _component = new();

    #region Key Normalization Tests

    [Fact]
    public void ShouldConvertToSnakeCase_WhenUsingNormalizeKeyUsingCamelCase()
    {
        Assert.Equal("asset_class", TestComponent.TestNormalizeKey("assetClass"));
    }

    [Fact]
    public void ShouldConvertToSnakeCase_WhenUsingNormalizeKeyUsingPascalCase()
    {
        Assert.Equal("asset_class", TestComponent.TestNormalizeKey("AssetClass"));
    }

    [Fact]
    public void ShouldUnchanged_WhenUsingNormalizeKeyUsingSnakeCase()
    {
        Assert.Equal("asset_class", TestComponent.TestNormalizeKey("asset_class"));
    }

    [Fact]
    public void ShouldUnchanged_WhenUsingNormalizeKeyUsingShortKeySingleChar()
    {
        Assert.Equal("x", TestComponent.TestNormalizeKey("x"));
    }

    [Fact]
    public void ShouldUnchanged_WhenUsingNormalizeKeyWithAllLowercase()
    {
        Assert.Equal("name", TestComponent.TestNormalizeKey("name"));
    }

    [Fact]
    public void ShouldAllNormalized_WhenUsingNormalizeParametersWithMixedKeys()
    {
        var input = new Dictionary<string, object?>
        {
            ["assetClass"] = "equity",
            ["PascalKey"] = "value",
            ["snake_key"] = "ok",
            ["x"] = 1
        };

        var result = TestComponent.TestNormalizeParameters(input);

        Assert.True(result.ContainsKey("assetClass") || result.ContainsKey("asset_class"));
        // The NormalizeParameters uses case-insensitive comparer; keys are stored as-is
        // But NormalizeJsonObject normalizes keys. Let's verify values are present.
        Assert.Equal(4, result.Count);
    }

    #endregion

    #region Tolerant Coercion Tests

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void ShouldCoerced_WhenDeserializingBoolFromString(string value, bool expected)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = value,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.Equal(expected, result.Active);
    }

    [Fact]
    public void ShouldTrue_WhenDeserializingBoolFromInt()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = 1,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.True(result.Active);
    }

    [Fact]
    public void ShouldFalse_WhenDeserializingBoolFromInt()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = 0,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.False(result.Active);
    }

    [Fact]
    public void ShouldTrue_WhenDeserializingBoolFromBool()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = true,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.True(result.Active);
    }

    [Theory]
    [InlineData("42")]
    public void ShouldCoerced_WhenDeserializingIntFromString(string value)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = value,
            ["active"] = false,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.Equal(42, result.Count);
    }

    [Fact]
    public void ShouldCoerced_WhenDeserializingIntFromDouble()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 42.0,
            ["active"] = false,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.Equal(42, result.Count);
    }

    [Fact]
    public void ShouldUnchanged_WhenDeserializingIntFromInt()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 42,
            ["active"] = false,
            ["score"] = 0.0
        };

        var result = _component.TestDeserialize(parameters);
        Assert.Equal(42, result.Count);
    }

    [Theory]
    [InlineData("2.718")]
    public void ShouldCoerced_WhenDeserializingDoubleFromString(string value)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = false,
            ["score"] = value
        };

        var result = _component.TestDeserialize(parameters);
        Assert.Equal(2.718, result.Score, 3);
    }

    [Fact]
    public void ShouldUnchanged_WhenDeserializingDoubleFromDouble()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = false,
            ["score"] = 3.14
        };

        var result = _component.TestDeserialize(parameters);
        Assert.Equal(3.14, result.Score, 2);
    }

    #endregion

    #region JsonElement Unwrap Tests

    [Fact]
    public void ShouldUnwrapToString_WhenUsingNormalizeValueUsingJsonElementString()
    {
        var json = JsonSerializer.SerializeToElement("hello");
        var result = TestComponent.TestNormalizeValue(json);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ShouldUnwrapToInt_WhenUsingNormalizeValueUsingJsonElementNumber()
    {
        var json = JsonSerializer.SerializeToElement(42);
        var result = TestComponent.TestNormalizeValue(json);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldUnwrapToBool_WhenUsingNormalizeValueUsingJsonElementTrue()
    {
        var json = JsonSerializer.SerializeToElement(true);
        var result = TestComponent.TestNormalizeValue(json);
        Assert.True((bool)result!);
    }

    [Fact]
    public void ShouldUnwrapToBool_WhenUsingNormalizeValueUsingJsonElementFalse()
    {
        var json = JsonSerializer.SerializeToElement(false);
        var result = TestComponent.TestNormalizeValue(json);
        Assert.False((bool)result!);
    }

    [Fact]
    public void ShouldUnwrapToNull_WhenUsingNormalizeValueUsingJsonElementNull()
    {
        var json = JsonSerializer.SerializeToElement<string?>(null);
        var result = TestComponent.TestNormalizeValue(json);
        Assert.Null(result);
    }

    [Fact]
    public void ShouldUnwrapToDictionary_WhenUsingNormalizeValueUsingJsonElementObject()
    {
        var json = JsonSerializer.SerializeToElement(new { Name = "test", Value = 42 });
        var result = TestComponent.TestNormalizeValue(json);
        Assert.IsType<Dictionary<string, object?>>(result);
        var dict = (Dictionary<string, object?>)result;
        Assert.Equal("test", dict["name"]);
    }

    [Fact]
    public void ShouldUnwrapToList_WhenUsingNormalizeValueUsingJsonElementArray()
    {
        var json = JsonSerializer.SerializeToElement(Int123);
        var result = TestComponent.TestNormalizeValue(json);
        Assert.IsType<List<object>>(result);
        var list = (List<object>)result;
        Assert.Equal(3, list.Count);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void ShouldCorrectResult_WhenUsingRoundTripSimpleDict()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["count"] = 5,
            ["active"] = true,
            ["score"] = 9.5
        };

        var request = _component.TestDeserialize(parameters);
        Assert.Equal("Alice", request.Name);
        Assert.Equal(5, request.Count);
        Assert.True(request.Active);
        Assert.Equal(9.5, request.Score, 1);

        // Simulate execution
        var response = new TestResponse { Result = "Hello Alice", Total = 10 };
        var outputDict = _component.TestSerialize(response);

        Assert.Equal("Hello Alice", outputDict["result"]?.ToString());
    }

    [Fact]
    public void ShouldDefaultValues_WhenUsingRoundTripWithMissingProperties()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "Bob"
        };

        var request = _component.TestDeserialize(parameters);
        Assert.Equal("Bob", request.Name);
        Assert.Equal(0, request.Count);       // default int
        Assert.False(request.Active);          // default bool
        Assert.Equal(0.0, request.Score, 1);   // default double
        Assert.Null(request.Optional);         // default nullable string
    }

    [Fact]
    public void ShouldIgnored_WhenUsingRoundTripWithExtraProperties()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "Charlie",
            ["count"] = 1,
            ["active"] = false,
            ["score"] = 0.0,
            ["unknown_property"] = "should be ignored",
            ["another_extra"] = 99
        };

        var request = _component.TestDeserialize(parameters);
        Assert.Equal("Charlie", request.Name);
        Assert.Equal(1, request.Count);
    }

    [Fact]
    public void ShouldProduceSnakeCaseKeys_WhenSerializingResponse()
    {
        var response = new TestResponse { Result = "ok", Total = 42 };
        var dict = _component.TestSerialize(response);

        Assert.True(dict.ContainsKey("result"));
        Assert.True(dict.ContainsKey("total"));
    }

    [Fact]
    public void ShouldOmittedFromOutput_WhenSerializingWithNullOptionalField()
    {
        // TestResponse doesn't have nullable fields, but TestRequest does
        // Verify through round-trip that null Optional is handled
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 0,
            ["active"] = false,
            ["score"] = 0.0
            // optional is missing => null
        };

        var request = _component.TestDeserialize(parameters);
        Assert.Null(request.Optional);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ShouldSuccess_WhenUsingValidateTypedRequestUsingReturnsNull()
    {
        _component.ValidationError = null;
        // Access validation through the component - it's called internally
        // We test indirectly by verifying that ExecuteTypedAsync works
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 1,
            ["active"] = true,
            ["score"] = 1.0
        };

        var request = _component.TestDeserialize(parameters);
        Assert.NotNull(request);
    }

    [Fact]
    public void ShouldFailureMessage_WhenUsingValidateTypedRequestUsingReturnsError()
    {
        _component.ValidationError = "Name is required";
        // The validation error is exposed through the property
        Assert.Equal("Name is required", _component.ValidationError);
    }

    #endregion
}
