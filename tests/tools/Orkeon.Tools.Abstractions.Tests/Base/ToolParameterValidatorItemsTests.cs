using System.Text.Json;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

/// <summary>
/// A flat enum is matched as listed, a JSON string unwrapped; the items of an array of objects are
/// judged by their properties before the tool runs, each named by its place (#210).
/// </summary>
public class ToolParameterValidatorItemsTests
{
    private static readonly ToolSchema Positions = new("t", "t", new Dictionary<string, ParameterSchema>
    {
        ["position"] = new("string", "where", Required: true, Enum: ["inside", "before", "after"]),
    });

    private static readonly ToolSchema Batch = new("edit_batch", "t", new Dictionary<string, ParameterSchema>
    {
        ["operations"] = new("array", "ops", Required: true, ItemsType: "object")
        {
            ItemProperties = new Dictionary<string, ParameterSchema>
            {
                ["kind"] = new("string", "what", Required: true, Enum: ["node_replace", "node_insert"]),
                ["path"] = new("string", "file", Required: true),
                ["position"] = new("string", "where", Required: false, Enum: ["inside", "before", "after"]),
            },
        },
    });

    [Fact]
    public void A_listed_enum_value_passes_and_an_unlisted_or_recased_one_is_refused_with_the_list()
    {
        Assert.True(ToolParameterValidator.ValidateParameters(new() { ["position"] = "inside" }, Positions).IsValid);

        var recased = ToolParameterValidator.ValidateParameters(new() { ["position"] = "Inside" }, Positions);
        Assert.False(recased.IsValid);
        Assert.Equal("Parameter 'position' must be one of: inside, before, after", recased.Error);
    }

    [Fact]
    public void A_listed_enum_value_passes_as_a_json_string_too()
    {
        using var document = JsonDocument.Parse("\"after\"");

        Assert.True(ToolParameterValidator.ValidateParameters(new() { ["position"] = document.RootElement.Clone() }, Positions).IsValid);
    }

    [Fact]
    public void An_item_missing_a_required_property_is_refused_by_its_place()
    {
        var operations = new List<object?>
        {
            new Dictionary<string, object?> { ["kind"] = "node_replace", ["path"] = "/workspace/A.cs" },
            new Dictionary<string, object?> { ["kind"] = "node_insert", ["position"] = "after" },
        };

        var result = ToolParameterValidator.ValidateParameters(new() { ["operations"] = operations }, Batch);

        Assert.False(result.IsValid);
        Assert.Equal("Required parameter 'operations[1].path' is missing", result.Error);
    }

    [Fact]
    public void An_item_with_an_unlisted_enum_value_is_refused_by_its_place()
    {
        var operations = new List<object?>
        {
            new Dictionary<string, object?> { ["kind"] = "node_replcae", ["path"] = "/workspace/A.cs" },
        };

        var result = ToolParameterValidator.ValidateParameters(new() { ["operations"] = operations }, Batch);

        Assert.False(result.IsValid);
        Assert.Equal("Parameter 'operations[0].kind' must be one of: node_replace, node_insert", result.Error);
    }

    [Fact]
    public void Items_given_as_a_json_array_are_judged_the_same()
    {
        using var document = JsonDocument.Parse("""[{ "kind": "node_insert", "path": "/workspace/A.cs", "position": null }, { "kind": "node_insert" }]""");

        var result = ToolParameterValidator.ValidateParameters(new() { ["operations"] = document.RootElement.Clone() }, Batch);

        Assert.False(result.IsValid);
        Assert.Equal("Required parameter 'operations[1].path' is missing", result.Error);
    }

    [Fact]
    public void Well_formed_items_pass()
    {
        var operations = new List<object?>
        {
            new Dictionary<string, object?> { ["kind"] = "node_replace", ["path"] = "/workspace/A.cs" },
            new Dictionary<string, object?> { ["kind"] = "node_insert", ["path"] = "/workspace/B.cs", ["position"] = "inside" },
        };

        Assert.True(ToolParameterValidator.ValidateParameters(new() { ["operations"] = operations }, Batch).IsValid);
    }
}
