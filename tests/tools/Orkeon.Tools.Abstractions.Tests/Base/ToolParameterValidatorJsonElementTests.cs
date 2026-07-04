using System.Text.Json;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

/// <summary>
/// Verifies that <see cref="ToolParameterValidator.IsValidType"/> correctly handles
/// <see cref="JsonElement"/> values that may leak from upstream deserialization.
/// This is defense-in-depth for the P1 bug where System.Text.Json produces
/// JsonElement wrappers instead of .NET primitives.
/// </summary>
public class ToolParameterValidatorJsonElementTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void IsValidType_JsonElement_String_ReturnsTrue()
    {
        var element = Parse("\"hello\"");
        Assert.True(ToolParameterValidator.IsValidType(element, "string"));
    }

    [Fact]
    public void IsValidType_JsonElement_Integer_Number_ReturnsTrue()
    {
        var element = Parse("42");
        Assert.True(ToolParameterValidator.IsValidType(element, "number"));
    }

    [Fact]
    public void IsValidType_JsonElement_Double_Number_ReturnsTrue()
    {
        var element = Parse("3.14");
        Assert.True(ToolParameterValidator.IsValidType(element, "number"));
    }

    [Fact]
    public void IsValidType_JsonElement_Integer_ReturnsTrue()
    {
        var element = Parse("42");
        Assert.True(ToolParameterValidator.IsValidType(element, "integer"));
    }

    [Fact]
    public void IsValidType_JsonElement_Boolean_True_ReturnsTrue()
    {
        var element = Parse("true");
        Assert.True(ToolParameterValidator.IsValidType(element, "boolean"));
    }

    [Fact]
    public void IsValidType_JsonElement_Boolean_False_ReturnsTrue()
    {
        var element = Parse("false");
        Assert.True(ToolParameterValidator.IsValidType(element, "boolean"));
    }

    [Fact]
    public void IsValidType_JsonElement_Array_ReturnsTrue()
    {
        var element = Parse("[1, 2, 3]");
        Assert.True(ToolParameterValidator.IsValidType(element, "array"));
    }

    [Fact]
    public void IsValidType_JsonElement_Object_ReturnsTrue()
    {
        var element = Parse("{\"key\": \"value\"}");
        Assert.True(ToolParameterValidator.IsValidType(element, "object"));
    }

    [Fact]
    public void IsValidType_JsonElement_String_WhenExpectingNumber_ReturnsFalse()
    {
        var element = Parse("\"not_a_number\"");
        Assert.False(ToolParameterValidator.IsValidType(element, "number"));
    }

    [Fact]
    public void IsValidType_JsonElement_Number_WhenExpectingString_ReturnsFalse()
    {
        var element = Parse("42");
        Assert.False(ToolParameterValidator.IsValidType(element, "string"));
    }

    [Fact]
    public void IsValidType_JsonElement_Double_WhenExpectingInteger_ReturnsFalse()
    {
        // 3.14 cannot be represented as an Int64
        var element = Parse("3.14");
        Assert.False(ToolParameterValidator.IsValidType(element, "integer"));
    }
}
