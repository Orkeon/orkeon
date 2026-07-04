using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// Regression tests for <see cref="JsonComponentSerializer"/>. Ensures the dict
/// returned by <c>Serialize&lt;T&gt;</c> contains only plain CLR types — never raw
/// <see cref="JsonElement"/> structs that hold offsets into pooled buffers.
/// </summary>
public class JsonComponentSerializerTests
{
    public sealed record SampleResponse
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("count")] public required int Count { get; init; }
        [JsonPropertyName("elapsed")] public required TimeSpan Elapsed { get; init; }
        [JsonPropertyName("errors")] public ImmutableArray<string> Errors { get; init; } = [];
        [JsonPropertyName("nested")] public NestedRecord? Nested { get; init; }
    }

    public sealed record NestedRecord
    {
        [JsonPropertyName("provider")] public required string Provider { get; init; }
        [JsonPropertyName("dimensions")] public required int Dimensions { get; init; }
    }

    [Fact]
    public void Serialize_does_not_return_JsonElement_values()
    {
        var response = new SampleResponse
        {
            Name = "test",
            Count = 42,
            Elapsed = TimeSpan.FromSeconds(1.5),
            Errors = ["err1", "err2"],
            Nested = new NestedRecord { Provider = "OpenAI", Dimensions = 1536 },
        };

        var dict = JsonComponentSerializer.Instance.Serialize(response);

        foreach (var (key, value) in dict)
        {
            Assert.False(value is JsonElement,
                $"key '{key}' still holds a JsonElement after Serialize — Jint will crash on it.");
        }
        // Nested object also unwrapped.
        var nested = Assert.IsType<Dictionary<string, object>>(dict["nested"]!);
        Assert.False(nested["provider"] is JsonElement);
        // Array also unwrapped to a List.
        var errors = Assert.IsType<List<object>>(dict["errors"]!);
        foreach (var item in errors)
        {
            Assert.False(item is JsonElement);
        }
    }

    [Fact]
    public void Serialize_yields_plain_string_for_TimeSpan()
    {
        var response = new SampleResponse
        {
            Name = "x",
            Count = 0,
            Elapsed = TimeSpan.FromMilliseconds(250),
        };

        var dict = JsonComponentSerializer.Instance.Serialize(response);

        // STJ serialises TimeSpan as a string ("00:00:00.2500000").
        Assert.IsType<string>(dict["elapsed"]);
    }

    [Fact]
    public void Serialize_handles_null_nested_record_without_emitting_JsonElement_null()
    {
        var response = new SampleResponse
        {
            Name = "x",
            Count = 1,
            Elapsed = TimeSpan.Zero,
            Nested = null,
        };

        var dict = JsonComponentSerializer.Instance.Serialize(response);

        // The null nested record is dropped (DefaultIgnoreCondition=WhenWritingNull).
        Assert.False(dict.ContainsKey("nested"));
    }

    public sealed record IndexyRequest
    {
        [JsonPropertyName("root_path")] public string RootPath { get; init; } = "";
        [JsonPropertyName("respect_gitignore")] public bool RespectGitignore { get; init; } = true;
    }

    /// <summary>
    /// Regression: a JS caller (Jint) passing camelCase keys would previously
    /// land with empty required-snake-case fields because the framework
    /// normalized values but not top-level keys, and STJ's
    /// PropertyNamingPolicy / PropertyNameCaseInsensitive does not equate
    /// "rootPath" with "root_path".
    /// </summary>
    [Fact]
    public void Deserialize_accepts_camelCase_top_level_keys_and_routes_to_snake_case_properties()
    {
        var camelInput = new Dictionary<string, object?>
        {
            ["rootPath"] = "/src",
            ["respectGitignore"] = false,
        };

        var req = JsonComponentSerializer.Instance.Deserialize<IndexyRequest>(camelInput);

        Assert.Equal("/src", req.RootPath);
        Assert.False(req.RespectGitignore);
    }

    [Fact]
    public void Deserialize_still_accepts_snake_case_top_level_keys()
    {
        var snakeInput = new Dictionary<string, object?>
        {
            ["root_path"] = "/src",
            ["respect_gitignore"] = false,
        };

        var req = JsonComponentSerializer.Instance.Deserialize<IndexyRequest>(snakeInput);

        Assert.Equal("/src", req.RootPath);
        Assert.False(req.RespectGitignore);
    }

    [Fact]
    public void Deserialize_also_accepts_PascalCase_top_level_keys()
    {
        var pascalInput = new Dictionary<string, object?>
        {
            ["RootPath"] = "/src",
        };

        var req = JsonComponentSerializer.Instance.Deserialize<IndexyRequest>(pascalInput);

        Assert.Equal("/src", req.RootPath);
    }
}
