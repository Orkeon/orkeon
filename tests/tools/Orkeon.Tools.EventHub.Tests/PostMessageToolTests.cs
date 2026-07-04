using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.EventHub.Dtos;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class PostMessageToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public PostMessageToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Posts_to_registered_mailbox_and_returns_id()
    {
        var address = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));
        using var registration = _fx.Hub.RegisterMailbox(address);

        var request = new PostMessageRequest
        {
            TargetMailbox = address.Raw,
            Payload = JsonNode.Parse("""{ "msg": "hi" }""")
        };
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(request))!;

        var response = await _fx.PostMessage.CallAsync(new ToolCallRequest("post_message", parameters), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var result = response.ResultDict();
        Assert.True(result.ContainsKey("message_id"));
        Assert.True(result.ContainsKey("posted_at"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Posting_to_unregistered_mailbox_returns_MailboxNotFound_error()
    {
        var address = $"agent://{CrewId.Create()}/{AgentId.Create()}";
        var parameters = new Dictionary<string, object?>
        {
            ["target_mailbox"] = address,
            ["payload"] = JsonNode.Parse("""{ }""")
        };

        var response = await _fx.PostMessage.CallAsync(new ToolCallRequest("post_message", parameters), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal(
            nameof(MailboxNotFoundException),
            response.Metadata?["exception_type"]?.ToString());
    }
}
