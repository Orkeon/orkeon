using System.Text.Json;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// Tests for <see cref="JsonElementConverter"/> — the shared utility that converts
/// <see cref="JsonElement"/> trees into .NET primitive types.
/// </summary>
public class JsonElementConverterTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ToDict_ConvertsStringToNetString()
    {
        var element = Parse("""{"path": "/tmp/file.txt"}""");
        var dict = JsonElementConverter.ToDict(element);

        Assert.IsType<string>(dict["path"]);
        Assert.Equal("/tmp/file.txt", dict["path"]);
    }

    [Fact]
    public void ToDict_ConvertsIntegerNumberToLong()
    {
        var element = Parse("""{"count": 42}""");
        var dict = JsonElementConverter.ToDict(element);

        Assert.IsType<long>(dict["count"]);
        Assert.Equal(42L, dict["count"]);
    }

    [Fact]
    public void ToDict_ConvertsDecimalNumberToDouble()
    {
        var element = Parse("""{"ratio": 3.14}""");
        var dict = JsonElementConverter.ToDict(element);

        Assert.IsType<double>(dict["ratio"]);
        Assert.Equal(3.14, dict["ratio"]);
    }

    [Fact]
    public void ToDict_ConvertsTrueBooleanToNetBool()
    {
        var element = Parse("""{"verbose": true}""");
        var dict = JsonElementConverter.ToDict(element);

        Assert.IsType<bool>(dict["verbose"]);
        Assert.True((bool)dict["verbose"]!);
    }

    [Fact]
    public void ToDict_ConvertsFalseBooleanToNetBool()
    {
        var element = Parse("""{"verbose": false}""");
        var dict = JsonElementConverter.ToDict(element);

        Assert.IsType<bool>(dict["verbose"]);
        Assert.False((bool)dict["verbose"]!);
    }

    [Fact]
    public void ToDict_ConvertsNestedObjectRecursively()
    {
        var element = Parse("""{"outer": {"inner": "value", "num": 7}}""");
        var dict = JsonElementConverter.ToDict(element);

        var nested = Assert.IsType<Dictionary<string, object?>>(dict["outer"]);
        Assert.Equal("value", nested["inner"]);
        Assert.Equal(7L, nested["num"]);
    }

    [Fact]
    public void ToDict_ConvertsArrayRecursively()
    {
        var element = Parse("""{"items": [1, "two", true, null]}""");
        var dict = JsonElementConverter.ToDict(element);

        var list = Assert.IsType<List<object?>>(dict["items"]);
        Assert.Equal(4, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal("two", list[1]);
        Assert.True((bool)list[2]!);
        Assert.Null(list[3]);
    }

    [Fact]
    public void ToDict_HandlesNullValueKind()
    {
        var element = Parse("""{"key": null}""");
        var dict = JsonElementConverter.ToDict(element);

        Assert.True(dict.ContainsKey("key"));
        Assert.Null(dict["key"]);
    }

    [Fact]
    public void ToDict_ReturnsEmptyDictForNonObject()
    {
        var element = Parse("\"just a string\"");
        var dict = JsonElementConverter.ToDict(element);

        Assert.Empty(dict);
    }

    [Fact]
    public void ConvertElement_StringReturnsString()
    {
        var element = Parse("\"hello\"");
        var result = JsonElementConverter.ConvertElement(element);

        Assert.IsType<string>(result);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ConvertElement_IntegerReturnsLong()
    {
        var element = Parse("100");
        var result = JsonElementConverter.ConvertElement(element);

        Assert.IsType<long>(result);
        Assert.Equal(100L, result);
    }

    [Fact]
    public void ConvertElement_DoubleReturnsDouble()
    {
        var element = Parse("2.718");
        var result = JsonElementConverter.ConvertElement(element);

        Assert.IsType<double>(result);
        Assert.Equal(2.718, result);
    }

    [Fact]
    public void ConvertArray_ReturnsListOfPrimitives()
    {
        var element = Parse("""["a", 1, false]""");
        var list = JsonElementConverter.ConvertArray(element);

        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal(1L, list[1]);
        Assert.False((bool)list[2]!);
    }

    [Fact]
    public void ToDict_ComplexNestedStructure()
    {
        var json = """
        {
            "name": "test",
            "config": {
                "retries": 3,
                "tags": ["alpha", "beta"],
                "enabled": true
            },
            "data": null
        }
        """;
        var element = Parse(json);
        var dict = JsonElementConverter.ToDict(element);

        Assert.Equal("test", dict["name"]);
        Assert.Null(dict["data"]);

        var config = Assert.IsType<Dictionary<string, object?>>(dict["config"]);
        Assert.Equal(3L, config["retries"]);
        Assert.True((bool)config["enabled"]!);

        var tags = Assert.IsType<List<object?>>(config["tags"]);
        Assert.Equal(["alpha", "beta"], tags);
    }
}
