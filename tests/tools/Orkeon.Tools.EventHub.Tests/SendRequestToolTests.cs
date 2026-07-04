using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class SendRequestToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public SendRequestToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Sends_request_and_returns_response_payload()
    {
        var address = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));
        using var registration = _fx.Hub.RegisterMailbox(address);

        // Responder background task — reply to the next message on the mailbox.
        var responder = System.Threading.Tasks.Task.Run(async () =>
        {
            var msg = await _fx.Hub.WaitForAsync(
                new WaitOnMailbox(address),
                FiniteWaitTimeout.Of(TimeSpan.FromSeconds(2)),
                CancellationToken.None);
            await _fx.Hub.ReplyAsync(msg.CorrelationId!, new { result = 42 }, CancellationToken.None);
        }, TestContext.Current.CancellationToken);

        var parameters = new Dictionary<string, object?>
        {
            ["target_mailbox"] = address.Raw,
            ["payload"] = JsonNode.Parse("""{ "q": 1 }"""),
            ["timeout_ms"] = 2000
        };

        var response = await _fx.SendRequest.CallAsync(new ToolCallRequest("send_request", parameters), TestContext.Current.CancellationToken);
        await responder;

        Assert.True(response.Success);
        var result = response.ResultDict();
        Assert.True(result.ContainsKey("response_payload"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Without_timeout_ms_validation_rejects()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["target_mailbox"] = $"agent://{CrewId.Create()}/{AgentId.Create()}",
            ["payload"] = JsonNode.Parse("""{ }""")
        };

        var response = await _fx.SendRequest.CallAsync(new ToolCallRequest("send_request", parameters), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("timeout_ms is required", response.Error, StringComparison.Ordinal);
    }
}
