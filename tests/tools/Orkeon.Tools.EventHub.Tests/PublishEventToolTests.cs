using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.EventHub.Dtos;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class PublishEventToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public PublishEventToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Roundtrip_json_publishes_and_returns_envelope_fields()
    {
        var request = new PublishEventRequest
        {
            Topic = "orders.created",
            Payload = JsonNode.Parse("""{ "id": "o-7" }""")
        };
        var json = JsonSerializer.Serialize(request);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

        var response = await _fx.PublishEvent.CallAsync(new ToolCallRequest("publish_event", parameters), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var result = response.ResultDict();
        Assert.True(result.ContainsKey("event_id"));
        Assert.True(result.ContainsKey("published_at"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Publishing_on_reserved_system_topic_returns_error_from_ReservedTopicException()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["topic"] = "_system.foo",
            ["payload"] = JsonNode.Parse("""{ "ignored": true }""")
        };

        var response = await _fx.PublishEvent.CallAsync(new ToolCallRequest("publish_event", parameters), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("_system.foo", response.Error, StringComparison.Ordinal);
        Assert.Contains(nameof(ReservedTopicException), response.Metadata?["exception_type"]?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task Missing_topic_is_rejected_by_validation()
    {
        var parameters = new Dictionary<string, object?> { ["topic"] = "" };
        var response = await _fx.PublishEvent.CallAsync(new ToolCallRequest("publish_event", parameters), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
    }
}
