using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// Exercises <see cref="Converters.ImmutableArrayStringTolerantConverter"/>
/// via the shared <see cref="JsonComponentSerializer.SnakeCaseOptions"/>
/// pipeline. Covers the JSON-encoded-array quirk observed with MiniMax-M2
/// via the Anthropic-compatible tool-use endpoint on params like
/// <c>sub_graph.seeds</c>.
/// </summary>
public class ImmutableArrayStringTolerantConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonComponentSerializer.SnakeCaseOptions;

    private static readonly string[] AlphaBeta = ["alpha", "beta"];
    private static readonly string[] AlphaOnly = ["alpha"];
    private static readonly string[] NotAnArray = ["[not-an-array"];
    private static readonly string[] PkgAB = ["pkg::A", "pkg::B"];

    private static ImmutableArray<string> Read(string json) =>
        JsonSerializer.Deserialize<ImmutableArray<string>>(json, Options);

    [Fact]
    public void Read_NormalArray_Roundtrips()
    {
        var result = Read("""["alpha", "beta"]""");
        Assert.Equal(AlphaBeta, result);
    }

    [Fact]
    public void Read_EmptyArray_ReturnsEmpty()
    {
        var result = Read("[]");
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Read_Null_ReturnsEmpty()
    {
        var result = Read("null");
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Read_ScalarString_CoercesToSingleElementArray()
    {
        var result = Read(""" "alpha" """);
        Assert.Equal(AlphaOnly, result);
    }

    [Fact]
    public void Read_JsonEncodedArrayString_ReturnsElements()
    {
        // MiniMax quirk: `["a","b"]` delivered as a JSON-escaped string.
        var result = Read("""
        "[\"alpha\", \"beta\"]"
        """);
        Assert.Equal(AlphaBeta, result);
    }

    [Fact]
    public void Read_JsonEncodedArrayString_WithWhitespace_Works()
    {
        var result = Read("""
        "  [\"alpha\"]  "
        """);
        Assert.Equal(AlphaOnly, result);
    }

    [Fact]
    public void Read_StringStartingWithBracketButNotArray_StaysScalar()
    {
        // `[not-an-array` — the JSON parse fails, fall through to scalar coercion.
        var result = Read("""
        "[not-an-array"
        """);
        Assert.Equal(NotAnArray, result);
    }

    [Fact]
    public void Read_SubGraphRequest_WithJsonEncodedSeeds()
    {
        // Emulates the MiniMax payload observed in R13: seeds wrapped as a JSON string.
        const string payload = """
        {
          "seeds": "[\"pkg::A\", \"pkg::B\"]",
          "edge_kinds": ["Calls"],
          "depth": 2,
          "direction": "forward",
          "max_nodes": 50,
          "include_mermaid": true
        }
        """;

        var request = JsonSerializer.Deserialize<SubGraphRequest>(payload, Options);

        Assert.NotNull(request);
        Assert.Equal(PkgAB, request!.Seeds);
        Assert.Equal(2, request.Depth);
    }
}
