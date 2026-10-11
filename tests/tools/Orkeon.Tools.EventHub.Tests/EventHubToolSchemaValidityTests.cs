using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class EventHubToolSchemaValidityTests : IClassFixture<EventHubToolsFixture>
{
    private static readonly HashSet<string> JsonSchemaTypes =
        ["string", "number", "integer", "boolean", "object", "array", "null"];

    private readonly EventHubToolsFixture _fx;
    public EventHubToolSchemaValidityTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    [Trait("BR", "CH-01")]
    public void Every_event_hub_tool_schema_carries_only_json_schema_types()
    {
        ToolSchema[] schemas =
        [
            _fx.PublishEvent.Schema, _fx.PostMessage.Schema, _fx.SendRequest.Schema, _fx.ReplyTo.Schema,
            _fx.ReceiveMessage.Schema, _fx.WaitForEvent.Schema, _fx.GetLastValue.Schema
        ];

        var typesFound = 0;
        foreach (var schema in schemas)
        {
            var node = JsonSerializer.SerializeToNode(schema.ToDocument())!;
            typesFound += Walk(node, schema.Name);
        }

        Assert.True(typesFound > 0);
    }

    private static int Walk(JsonNode? node, string path)
    {
        var count = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key == "type" && value is JsonValue v && v.TryGetValue<string>(out var t))
                    {
                        Assert.True(JsonSchemaTypes.Contains(t), $"{path}: '{t}' is not a JSON Schema type");
                        count++;
                    }
                    else
                    {
                        count += Walk(value, $"{path}.{key}");
                    }
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    count += Walk(arr[i], $"{path}[{i}]");
                break;
        }
        return count;
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void Metadata_is_an_object_for_post_message_publish_event_send_request_and_wait_for_event()
    {
        (ToolSchema Schema, string Parameter)[] maps =
        [
            (_fx.PostMessage.Schema, "metadata"),
            (_fx.PublishEvent.Schema, "metadata"),
            (_fx.SendRequest.Schema, "metadata"),
            (_fx.WaitForEvent.Schema, "metadata_match")
        ];

        foreach (var (schema, name) in maps)
        {
            var doc = schema.Parameters[name].ToDocument();

            Assert.Equal("object", doc["type"]);
            Assert.True(doc.ContainsKey("additionalProperties"), $"{schema.Name}.{name}: no additionalProperties");
            Assert.False(doc.ContainsKey("items"), $"{schema.Name}.{name}: items must not be written");
        }
    }
}
