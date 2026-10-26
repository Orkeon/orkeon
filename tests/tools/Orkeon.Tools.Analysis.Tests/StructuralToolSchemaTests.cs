using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

/// <summary>
/// The five structural tools publish a parameter schema generated from their request record, as the file
/// tools do (#210): the model reads the arguments from the schema, not from the description alone, and a
/// call missing a required argument is refused before the tool runs, with the argument's name.
/// </summary>
public sealed class StructuralToolSchemaTests : IDisposable
{
    private readonly TreeSitterParserPool _pool = new();
    private readonly FakeFileSystemService _fs = new();

    public void Dispose() => _pool.Dispose();

    private static string[] RequiredOf(ToolSchema schema) =>
        schema.Parameters.Where(p => p.Value.Required).Select(p => p.Key).Order().ToArray();

    [Fact]
    public void ShouldRequireThePath_WhenTheNodeOutlineSchemaIsRead()
    {
        using var tool = new NodeOutlineTool(_fs, _pool);

        var schema = tool.Schema;

        Assert.Equal("node_outline", schema.Name);
        Assert.Equal(["path"], RequiredOf(schema));
        Assert.Equal("string", schema.Parameters["path"].Type);
    }

    [Fact]
    public void ShouldRequireThePathAndTheSelector_WhenTheNodeReadSchemaIsRead()
    {
        using var tool = new NodeReadTool(_fs, _pool);

        Assert.Equal(["path", "selector"], RequiredOf(tool.Schema));
    }

    [Fact]
    public void ShouldPublishEveryArgument_WhenTheNodeInsertSchemaIsRead()
    {
        using var tool = new NodeInsertTool(_fs, _pool);

        var schema = tool.Schema;

        Assert.Equal(["path", "position", "selector", "text"], RequiredOf(schema));
        Assert.Equal(["inside", "before", "after"], schema.Parameters["position"].Enum);
    }

    [Fact]
    public void ShouldPublishEveryArgument_WhenTheNodeReplaceSchemaIsRead()
    {
        using var tool = new NodeReplaceTool(_fs, _pool);

        Assert.Equal(["expected_hash", "path", "selector", "text"], RequiredOf(tool.Schema));
    }

    [Fact]
    public void ShouldPublishTheOperationsAndTheirOptions_WhenTheNodeEnsureSchemaIsRead()
    {
        using var tool = new NodeEnsureTool(_fs, _pool);

        var schema = tool.Schema;

        Assert.Equal(["op", "path"], RequiredOf(schema));
        Assert.Equal(["index", "level", "module", "name", "op", "path", "selector", "text"], schema.Parameters.Keys.Order().ToArray());
        Assert.Contains("ensure_using", schema.Parameters["op"].Enum!);
        Assert.Contains("replace_mermaid", schema.Parameters["op"].Enum!);
        Assert.Equal("integer", schema.Parameters["index"].Type);
    }

    [Fact]
    public void ShouldPublishTheOperationsShape_WhenTheEditBatchSchemaIsRead()
    {
        using var tool = new EditBatchTool(_fs, _pool);

        var schema = tool.Schema;

        Assert.Equal(["operations"], RequiredOf(schema));
        var operations = schema.Parameters["operations"];
        Assert.Equal("array", operations.Type);
        Assert.Equal("object", operations.ItemsType);
        Assert.NotNull(operations.ItemProperties);
        Assert.Equal(
            ["expected_hash", "index", "kind", "level", "module", "name", "path", "position", "selector", "text"],
            operations.ItemProperties.Keys.Order().ToArray());
        Assert.Equal(["kind", "path"], operations.ItemProperties.Where(p => p.Value.Required).Select(p => p.Key).Order().ToArray());
        Assert.Contains("node_replace", operations.ItemProperties["kind"].Enum!);
        Assert.Contains("ensure_section", operations.ItemProperties["kind"].Enum!);
        Assert.Equal(["inside", "before", "after"], operations.ItemProperties["position"].Enum);

        var validate = schema.Parameters["validate"];
        Assert.False(validate.Required);
        Assert.Equal("parse", validate.Default);
        Assert.Equal(["parse", "build"], validate.Enum);
    }

    [Fact]
    public async Task ShouldRefuseTheCallNamingThePath_WhenNodeOutlineIsCalledWithoutIt()
    {
        using var tool = new NodeOutlineTool(_fs, _pool);

        var response = await tool.CallAsync(
            new ToolCallRequest("node_outline", new Dictionary<string, object?> { ["selector"] = "src/stats.js" }),
            TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal("Required parameter 'path' is missing", response.Error);
    }

    [Fact]
    public async Task ShouldRefuseTheCallNamingTheOperationsPath_WhenAnOperationLacksIt()
    {
        using var tool = new EditBatchTool(_fs, _pool);
        var operations = new List<object?>
        {
            new Dictionary<string, object?> { ["kind"] = "node_insert", ["selector"] = "Demo.Calc", ["position"] = "inside", ["text"] = "int x;" },
        };

        var response = await tool.CallAsync(
            new ToolCallRequest("edit_batch", new Dictionary<string, object?> { ["operations"] = operations }),
            TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal("Required parameter 'operations[0].path' is missing", response.Error);
    }

    [Fact]
    public async Task ShouldRefuseTheCallNamingTheOperations_WhenEditBatchIsCalledWithoutThem()
    {
        using var tool = new EditBatchTool(_fs, _pool);

        var response = await tool.CallAsync(
            new ToolCallRequest("edit_batch", new Dictionary<string, object?> { ["validate"] = "parse" }),
            TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal("Required parameter 'operations' is missing", response.Error);
    }
}
