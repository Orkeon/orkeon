using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// Exercises <see cref="Converters.ImmutableArrayEnumTolerantConverter{TEnum}"/>
/// via the shared <see cref="JsonComponentSerializer.SnakeCaseOptions"/> pipeline,
/// which registers the converter for <c>EdgeKind</c>.
/// </summary>
public class ImmutableArrayEnumTolerantConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonComponentSerializer.SnakeCaseOptions;

    private static ImmutableArray<EdgeKind> Read(string json) =>
        JsonSerializer.Deserialize<ImmutableArray<EdgeKind>>(json, Options);

    [Fact]
    public void Read_ArrayOfStrings_ReturnsMatchingEnums()
    {
        var result = Read("""["Calls"]""");
        Assert.Equal(new[] { EdgeKind.Calls }, result);
    }

    [Fact]
    public void Read_ScalarString_CoercesToSingleElementArray()
    {
        var result = Read(""" "Calls" """);
        Assert.Equal(new[] { EdgeKind.Calls }, result);
    }

    [Fact]
    public void Read_MixedCase_ParsesCaseInsensitively()
    {
        var result = Read("""["calls", "IMPORTS"]""");
        Assert.Equal(new[] { EdgeKind.Calls, EdgeKind.Imports }, result);
    }

    [Fact]
    public void Read_Null_ReturnsEmpty()
    {
        var result = Read("null");
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Read_EmptyArray_ReturnsEmpty()
    {
        var result = Read("[]");
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Read_UnknownValue_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => Read("""["Unknown"]"""));
    }

    [Fact]
    public void Read_JsonEncodedArrayString_ReturnsMatchingEnums()
    {
        // MiniMax-M2 via Anthropic endpoint emits array params as JSON-encoded strings.
        var result = Read("""
        "[\"Calls\"]"
        """);
        Assert.Equal(new[] { EdgeKind.Calls }, result);
    }

    [Fact]
    public void Read_JsonEncodedArrayString_MultipleElements_MixedCase()
    {
        var result = Read("""
        "[\"calls\", \"IMPORTS\"]"
        """);
        Assert.Equal(new[] { EdgeKind.Calls, EdgeKind.Imports }, result);
    }

    [Fact]
    public void Read_JsonEncodedArrayString_WithWhitespace()
    {
        var result = Read("""
        "  [\"Calls\"]  "
        """);
        Assert.Equal(new[] { EdgeKind.Calls }, result);
    }

    [Fact]
    public void Read_JsonEncodedArrayString_UnknownValue_Throws()
    {
        Assert.Throws<JsonException>(() => Read("""
        "[\"Unknown\"]"
        """));
    }

    [Fact]
    public void Read_ScalarThatLooksLikeOpeningBracket_StillTreatedAsScalar()
    {
        // Without closing bracket, the fallback to enum-name parsing must trigger and fail normally.
        Assert.Throws<JsonException>(() => Read("""
        "[Calls"
        """));
    }

    [Fact]
    public void Roundtrip_FlowTraceRequest_PreservesEdgeKinds()
    {
        // Emulates the MiniMax payload: edge_kinds as JSON array inside a full request body.
        const string payload = """
        {
          "from": "pkg::Module::fn",
          "direction": "forward",
          "max_depth": 3,
          "edge_kinds": ["Calls", "Imports"]
        }
        """;

        var request = JsonSerializer.Deserialize<FlowTraceRequest>(payload, Options);

        Assert.NotNull(request);
        Assert.Equal("pkg::Module::fn", request!.From);
        Assert.Equal(new[] { EdgeKind.Calls, EdgeKind.Imports }, request.EdgeKinds);
    }
}
