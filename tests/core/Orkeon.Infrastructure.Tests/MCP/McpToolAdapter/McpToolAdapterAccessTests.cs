using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// #219 - the access an MCP tool declares, read from the <c>annotations</c> its server sends in
/// <c>tools/list</c>: read-only hint, other annotations, or none (the gate's fail-closed default).
/// </summary>
public sealed class McpToolAdapterAccessTests : IAsyncDisposable
{
    private readonly MockMcpTransport _transport = new();
    private readonly McpClient _client;

    public McpToolAdapterAccessTests() => _client = new McpClient(_transport);

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _transport.DisposeAsync();
    }
    [Fact]
    public void ShouldDeclareReadWhenTheServerHintsReadOnly()
    {
        var adapter = AdapterOf("""{"name":"docs_search","description":"d","annotations":{"readOnlyHint":true,"openWorldHint":true}}""");

        Assert.Equal(ToolAccess.Read, ((IBaseTool)adapter).Access);
    }

    [Theory]
    [InlineData("""{"readOnlyHint":false}""")]
    [InlineData("""{"destructiveHint":false,"idempotentHint":true}""")]
    [InlineData("""{}""")]
    public void ShouldDeclareExecuteWhenTheAnnotationsDoNotHintReadOnly(string annotations)
    {
        var adapter = AdapterOf($$"""{"name":"write_page","description":"d","annotations":{{annotations}}}""");

        Assert.Equal(ToolAccess.Execute, ((IBaseTool)adapter).Access);
    }

    [Fact]
    public void ShouldStayUnspecifiedWhenTheServerSendsNoAnnotations()
    {
        var adapter = AdapterOf("""{"name":"legacy","description":"d"}""");

        Assert.Equal(ToolAccess.Unspecified, ((IBaseTool)adapter).Access);
        Assert.Equal(ToolAccess.Unspecified, ((IBaseTool)adapter).AccessFor(new Dictionary<string, object?>()));
    }

    [Fact]
    public void ShouldLeaveAbsentAnnotationsOutOfTheSerializedDefinition()
    {
        var json = JsonSerializer.Serialize(new McpToolDefinition { Name = "t", Description = "d" });

        Assert.DoesNotContain("annotations", json, StringComparison.Ordinal);
    }

    private Orkeon.Infrastructure.MCP.McpToolAdapter AdapterOf(string definitionJson) =>
        new(JsonSerializer.Deserialize<McpToolDefinition>(definitionJson)!, _client);
}
