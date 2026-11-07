using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// A scalar <see cref="FlagsAttribute"/> enum in a tool request or response goes through
/// <see cref="JsonComponentSerializer.SnakeCaseOptions"/> as snake_case names joined by <c>", "</c>,
/// and reads back from its names, case and separators tolerated (#274).
/// </summary>
public class TolerantFlagsEnumConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonComponentSerializer.SnakeCaseOptions;

    [Fact]
    public void ShouldWriteEdgeKindNames_WhenSerializingDependencyGraphResponse()
    {
        var response = new DependencyGraphResponse
        {
            Nodes = [],
            Edges =
            [
                new GraphEdge { From = "a", To = "b", Kind = EdgeKind.Imports },
                new GraphEdge { From = "b", To = "c", Kind = EdgeKind.ProjectDepends },
            ],
            Metrics = new GraphMetrics(0, 2, 1, 0, 0),
            Cycles = [new GraphCycle { Fqns = ["a", "b"], MemberCount = 2, EdgeKind = EdgeKind.Imports | EdgeKind.Calls }],
        };

        var json = JsonSerializer.Serialize(response, Options);

        Assert.Contains("""{"from":"a","to":"b","kind":"imports"}""", json, StringComparison.Ordinal);
        Assert.Contains("""{"from":"b","to":"c","kind":"project_depends"}""", json, StringComparison.Ordinal);
        Assert.Contains("\"edge_kind\":\"imports, calls\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldWriteEdgeKindName_WhenSerializingToToolDictionary()
    {
        var edge = new GraphEdge { From = "a", To = "b", Kind = EdgeKind.Extends };

        var serialized = JsonComponentSerializer.Instance.Serialize(edge);

        Assert.Equal("extends", serialized["kind"]?.ToString());
    }

    [Theory]
    [InlineData("\"imports\"", EdgeKind.Imports)]
    [InlineData("\"project_depends\"", EdgeKind.ProjectDepends)]
    [InlineData("\"ProjectDepends\"", EdgeKind.ProjectDepends)]
    [InlineData("\"imports, calls\"", EdgeKind.Imports | EdgeKind.Calls)]
    [InlineData("\"Imports|Calls\"", EdgeKind.Imports | EdgeKind.Calls)]
    [InlineData("[\"imports\",\"calls\"]", EdgeKind.Imports | EdgeKind.Calls)]
    [InlineData("\"none\"", EdgeKind.None)]
    public void ShouldReadEdgeKind_WhenNameCaseOrSeparatorVaries(string json, EdgeKind expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<EdgeKind>(json, Options));
    }

    [Theory]
    [InlineData(EdgeKind.Imports | EdgeKind.Documents)]
    [InlineData(EdgeKind.None)]
    [InlineData(EdgeKind.ProjectDepends)]
    public void ShouldRoundTrip_WhenEdgeKindIsWrittenThenRead(EdgeKind kind)
    {
        var json = JsonSerializer.Serialize(kind, Options);
        Assert.Equal(kind, JsonSerializer.Deserialize<EdgeKind>(json, Options));
    }

    [Fact]
    public void ShouldListValidNames_WhenNameIsUnknown()
    {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EdgeKind>("\"imports, bogus\"", Options));
        Assert.Contains("bogus", ex.Message, StringComparison.Ordinal);
        Assert.Contains("project_depends", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("256")]
    [InlineData("[1,2]")]
    public void ShouldRefuse_WhenEdgeKindIsANumber(string json)
    {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EdgeKind>(json, Options));
        Assert.Contains("project_depends", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("members", ExpandModes.Members)]
    [InlineData("Members", ExpandModes.Members)]
    [InlineData("callers, callees", ExpandModes.Callers | ExpandModes.Callees)]
    [InlineData("public_exports", ExpandModes.PublicExports)]
    [InlineData("all", ExpandModes.All)]
    public void ShouldReadExpand_WhenSymbolDetailRequestNamesIt(string expand, ExpandModes expected)
    {
        var request = JsonComponentSerializer.Instance.Deserialize<SymbolDetailRequest>(
            new Dictionary<string, object?> { ["fqn"] = "A.B", ["expand"] = expand });

        Assert.Equal(expected, request.Expand);
    }

    [Fact]
    public void ShouldKeepMemberName_WhenCombinationIsNamed()
    {
        Assert.Equal("\"all\"", JsonSerializer.Serialize(ExpandModes.All, Options));
        Assert.Equal("\"members, callers\"", JsonSerializer.Serialize(ExpandModes.Members | ExpandModes.Callers, Options));
    }

    [Fact]
    public void ShouldUseSameNames_WhenEdgeKindsAreInArray()
    {
        ImmutableArray<EdgeKind> kinds = [EdgeKind.ProjectDepends, EdgeKind.Calls];

        var json = JsonSerializer.Serialize(kinds, Options);

        Assert.Equal("[\"project_depends\",\"calls\"]", json);
        Assert.Equal(kinds, JsonSerializer.Deserialize<ImmutableArray<EdgeKind>>(json, Options));
    }
}
