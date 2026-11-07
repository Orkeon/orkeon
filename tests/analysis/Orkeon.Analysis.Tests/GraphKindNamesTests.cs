using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// Tests for <see cref="GraphKindNames"/>: only the snake_case workflow edge names parse; the enum name
/// of an edge kind and its numeric value are refused, and the refusal lists the valid names.
/// </summary>
public class GraphKindNamesTests
{
    [Theory]
    [InlineData("ProjectDepends")]
    [InlineData("128")]
    public void ParseEdgeKinds_EnumNameOrNumber_IsRefusedWithTheValidNames(string name)
    {
        Assert.False(GraphKindNames.TryParseEdgeKind(name, out _));

        var error = Assert.Throws<ArgumentException>(
            () => GraphKindNames.ParseEdgeKinds(ImmutableArray.Create(name)));

        Assert.Contains($"'{name}'", error.Message, StringComparison.Ordinal);
        foreach (var valid in GraphKindNames.EdgeKindNames)
        {
            Assert.Contains(valid, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ParseEdgeKinds_SnakeCaseName_IsAccepted()
    {
        var kinds = GraphKindNames.ParseEdgeKinds(ImmutableArray.Create("project_depends"));

        Assert.Single(kinds);
    }
}
