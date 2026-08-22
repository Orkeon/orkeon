using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class ReceiveMessageToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public ReceiveMessageToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Empty_request_fails_validation()
    {
        var parameters = new Dictionary<string, object?>();
        var response = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", parameters), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
    }

    [Fact]
    public async System.Threading.Tasks.Task Mailbox_default_requires_caller_agent_id()
    {
        // Default caller context exposes no AgentId — ReceiveMessage cannot resolve the default mailbox.
        var parameters = new Dictionary<string, object?> { ["timeout_ms"] = 50 };
        var response = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", parameters), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
        Assert.Contains("caller context exposes no AgentId", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task Explicit_mailbox_and_finite_timeout_returns_timed_out()
    {
        // The system caller (no crew pushed) is the process itself and stays unrestricted.
        var address = $"agent://{CrewId.Create()}/{AgentId.Create()}";
        var parameters = new Dictionary<string, object?>
        {
            ["mailbox"] = address,
            ["timeout_ms"] = 50
        };

        var response = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", parameters), TestContext.Current.CancellationToken);
        Assert.True(response.Success);
        var result = response.ResultDict();
        Assert.True((bool)result["timed_out"]!);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_crew_may_only_read_its_own_mailboxes()
    {
        // Reading is destructive — the hub auto-registers waiters and they compete for every
        // message. Without this refusal, receive_message(mailbox: "client://studio") would let
        // any agent siphon the traffic of the external peer the ACL guards on the write side.
        var crewId = CrewId.Create();
        using (_fx.Caller.Push(new EventHubCaller(crewId, AgentId.Create())))
        {
            var foreign = new Dictionary<string, object?>
            {
                ["mailbox"] = $"agent://{CrewId.Create()}/{AgentId.Create()}",
                ["timeout_ms"] = 50
            };
            var refusedCrew = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", foreign), TestContext.Current.CancellationToken);
            Assert.False(refusedCrew.Success);
            Assert.Contains("belongs to someone else", refusedCrew.Error, StringComparison.Ordinal);

            var client = new Dictionary<string, object?>
            {
                ["mailbox"] = "client://studio",
                ["timeout_ms"] = 50
            };
            var refusedClient = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", client), TestContext.Current.CancellationToken);
            Assert.False(refusedClient.Success);

            var own = new Dictionary<string, object?>
            {
                ["mailbox"] = $"crew://{crewId}",
                ["timeout_ms"] = 50
            };
            var allowed = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", own), TestContext.Current.CancellationToken);
            Assert.True(allowed.Success);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Default_mailbox_resolved_from_caller_context_returns_message()
    {
        var crewId = CrewId.Create();
        var agentId = AgentId.Create();

        using (_fx.Caller.Push(new EventHubCaller(crewId, agentId)))
        {
            var parameters = new Dictionary<string, object?> { ["timeout_ms"] = 2000 };
            var waitTask = _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", parameters), TestContext.Current.CancellationToken);

            var address = MailboxAddress.Parse(new Uri($"agent://{crewId}/{agentId}"));
            Assert.Contains(address.Raw, _fx.Hub.GetMailboxAddresses());

            await _fx.Hub.PostAsync(address, JsonNode.Parse("""{ "hello": true }""")!, CancellationToken.None);

            var response = await waitTask;
            Assert.True(response.Success);
            var result = response.ResultDict();
            Assert.False((bool)result["timed_out"]!);
        }
    }
}
