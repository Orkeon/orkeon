using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

/// <summary>
/// Exercises <see cref="Converters.TolerantEnumConverter{TEnum}"/> via the
/// shared <see cref="JsonComponentSerializer.SnakeCaseOptions"/> pipeline.
/// Covers the R14 observation where MiniMax emitted <c>"L0_Root"</c> for a
/// <see cref="NodeLevel"/> slot and got back an opaque "could not be
/// converted" error instead of a list of valid values.
/// </summary>
public class TolerantEnumConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonComponentSerializer.SnakeCaseOptions;

    private static NodeLevel ReadNodeLevel(string json) =>
        JsonSerializer.Deserialize<NodeLevel>(json, Options);

    [Fact]
    public void Read_ExactEnumName_Parses()
    {
        Assert.Equal(NodeLevel.L1_Package, ReadNodeLevel("\"L1_Package\""));
    }

    [Fact]
    public void Read_LowercaseVariant_Parses()
    {
        Assert.Equal(NodeLevel.L1_Package, ReadNodeLevel("\"l1_package\""));
    }

    [Fact]
    public void Read_CamelCaseVariant_Parses()
    {
        // JsonNamingPolicy.CamelCase outputs "l1_Package" — round-tripping our
        // own writes must keep working after the switch.
        Assert.Equal(NodeLevel.L1_Package, ReadNodeLevel("\"l1_Package\""));
    }

    [Fact]
    public void Read_UnderscoreStripped_Parses()
    {
        Assert.Equal(NodeLevel.L1_Package, ReadNodeLevel("\"L1Package\""));
        Assert.Equal(NodeLevel.L1_Package, ReadNodeLevel("\"l1package\""));
    }

    [Fact]
    public void Read_InvalidValue_ThrowsWithAllValidNames()
    {
        var ex = Assert.Throws<JsonException>(() => ReadNodeLevel("\"L0_Root\""));

        Assert.Contains("L0_Root", ex.Message);
        Assert.Contains("NodeLevel", ex.Message);
        // Every valid value must be cited so the LLM can pick one on retry.
        Assert.Contains("L0_Monorepo", ex.Message);
        Assert.Contains("L1_Package", ex.Message);
        Assert.Contains("L2_Module", ex.Message);
        Assert.Contains("L3_Symbol", ex.Message);
        Assert.Contains("L4_Statement", ex.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("42")]
    public void Read_Number_IsRefusedWithValidNames(string json)
    {
        var ex = Assert.Throws<JsonException>(() => ReadNodeLevel(json));
        Assert.Contains("L1_Package", ex.Message);
        Assert.Contains("L4_Statement", ex.Message);
    }

    [Fact]
    public void Write_CamelCaseRoundtrip()
    {
        var json = JsonSerializer.Serialize(NodeLevel.L2_Module, Options);
        Assert.Equal("\"l2_Module\"", json);

        // And it reads back through the tolerant path.
        Assert.Equal(NodeLevel.L2_Module, ReadNodeLevel(json));
    }

    [Fact]
    public void Read_DirectionEnum_AlsoCoveredByFactory()
    {
        // Factory pattern means every non-flags enum benefits — sanity check
        // with a different enum so we don't accidentally special-case NodeLevel.
        var result = JsonSerializer.Deserialize<Direction>("\"backward\"", Options);
        Assert.Equal(Direction.Backward, result);
    }

    [Fact]
    public void Read_FlagsEnum_StillUsesItsOwnConverter()
    {
        // EdgeKind is [Flags]: each element of an ImmutableArray<EdgeKind> is read
        // by TolerantFlagsEnumConverter, not by the non-flags tolerant converter.
        var result = JsonSerializer.Deserialize<System.Collections.Immutable.ImmutableArray<EdgeKind>>(
            "[\"Calls\"]", Options);
        Assert.Equal(new[] { EdgeKind.Calls }, result);
    }
}
