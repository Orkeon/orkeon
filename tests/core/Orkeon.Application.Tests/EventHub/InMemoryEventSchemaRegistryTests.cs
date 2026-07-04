using System.Text.Json.Nodes;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Application.Tests.EventHub;

public sealed class InMemoryEventSchemaRegistryTests
{
    [Fact]
    public async System.Threading.Tasks.Task GetAsync_returns_null_when_schemaId_absent()
    {
        var registry = new InMemoryEventSchemaRegistry();
        var result = await registry.GetAsync("missing/v1", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task RegisterAsync_then_GetAsync_returns_schema()
    {
        var registry = new InMemoryEventSchemaRegistry();
        var schema = JsonNode.Parse("""{ "type": "object" }""")!;
        await registry.RegisterAsync("schema-1", schema, CancellationToken.None);
        var result = await registry.GetAsync("schema-1", CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task RegisterAsync_same_schemaId_with_different_definition_throws()
    {
        var registry = new InMemoryEventSchemaRegistry();
        await registry.RegisterAsync("schema-1", JsonNode.Parse("""{ "type": "object" }""")!, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.RegisterAsync("schema-1", JsonNode.Parse("""{ "type": "string" }""")!, CancellationToken.None));
    }

    [Fact]
    public async System.Threading.Tasks.Task RegisterAsync_same_schemaId_with_equivalent_definition_is_idempotent()
    {
        var registry = new InMemoryEventSchemaRegistry();
        await registry.RegisterAsync("schema-1", JsonNode.Parse("""{ "type": "object" }""")!, CancellationToken.None);
        await registry.RegisterAsync("schema-1", JsonNode.Parse("""{ "type": "object" }""")!, CancellationToken.None);

        // No exception → idempotent; the schema remains retrievable.
        var result = await registry.GetAsync("schema-1", CancellationToken.None);
        Assert.NotNull(result);
    }
}
