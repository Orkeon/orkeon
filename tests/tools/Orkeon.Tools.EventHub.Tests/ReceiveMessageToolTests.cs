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

            var topic = new Dictionary<string, object?>
            {
                ["mailbox"] = "topic://updates",
                ["timeout_ms"] = 50
            };
            var refusedTopic = await _fx.ReceiveMessage.CallAsync(new ToolCallRequest("receive_message", topic), TestContext.Current.CancellationToken);
            // A topic:// mailbox is a shared named queue on the hub — draining it is the
            // same destructive competition as any other foreign mailbox.
            Assert.False(refusedTopic.Success);

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

            await _fx.Hub.PostAsync(address, JsonNode.Parse("""{ "hello": true }""")!, null, CancellationToken.None);

            var response = await waitTask;
            Assert.True(response.Success);
            var result = response.ResultDict();
            Assert.False((bool)result["timed_out"]!);
        }
    }
}

/// <summary>
/// GAP-11 — what <c>post_message</c> and <c>send_request</c> are given reaches the recipient:
/// the <c>metadata</c> pairs ride in the envelope, and the <c>message_id</c> a post returns is
/// the id the recipient reads. Both used to be swallowed or invented.
/// </summary>
public sealed class MailboxEnvelopeTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public MailboxEnvelopeTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Post_message_metadata_and_id_reach_receive_message()
    {
        var crewId = CrewId.Create();
        var agentId = AgentId.Create();
        var address = $"agent://{crewId}/{agentId}";

        using (_fx.Caller.Push(new EventHubCaller(crewId, agentId)))
        {
            var receiving = _fx.ReceiveMessage.CallAsync(
                new ToolCallRequest("receive_message", new Dictionary<string, object?> { ["timeout_ms"] = 2000 }),
                TestContext.Current.CancellationToken);

            var posted = await _fx.PostMessage.CallAsync(
                new ToolCallRequest("post_message", new Dictionary<string, object?>
                {
                    ["target_mailbox"] = address,
                    ["payload"] = JsonNode.Parse("""{ "msg": "hi" }"""),
                    ["metadata"] = new Dictionary<string, string> { ["priority"] = "high", ["ticket"] = "T-7" },
                }),
                TestContext.Current.CancellationToken);
            var received = await receiving;

            Assert.True(posted.Success, posted.Error);
            Assert.True(received.Success, received.Error);
            var envelope = System.Text.Json.JsonSerializer.SerializeToNode(received.ResultDict()["message"])!;
            Assert.Equal("high", envelope["metadata"]!["priority"]!.GetValue<string>());
            Assert.Equal("T-7", envelope["metadata"]!["ticket"]!.GetValue<string>());
            Assert.Equal(posted.ResultDict()["message_id"]?.ToString(), envelope["message_id"]!.GetValue<string>());
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Publish_event_returns_the_id_wait_for_event_reads()
    {
        var waiting = _fx.WaitForEvent.CallAsync(
            new ToolCallRequest("wait_for_event", new Dictionary<string, object?>
            {
                ["topic"] = "orders.ids",
                ["timeout_ms"] = 2000,
            }),
            TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Yield();

        var published = await _fx.PublishEvent.CallAsync(
            new ToolCallRequest("publish_event", new Dictionary<string, object?>
            {
                ["topic"] = "orders.ids",
                ["payload"] = JsonNode.Parse("""{ "id": "o-1" }"""),
            }),
            TestContext.Current.CancellationToken);
        var received = await waiting;

        Assert.True(published.Success, published.Error);
        Assert.True(received.Success, received.Error);
        var envelope = System.Text.Json.JsonSerializer.SerializeToNode(received.ResultDict()["message"])!;
        Assert.Equal(published.ResultDict()["event_id"]?.ToString(), envelope["message_id"]!.GetValue<string>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Send_request_metadata_reaches_the_responder()
    {
        var address = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));
        using var registration = _fx.Hub.RegisterMailbox(address);
        Message? seen = null;

        var responder = System.Threading.Tasks.Task.Run(async () =>
        {
            seen = await _fx.Hub.WaitForAsync(
                new WaitOnMailbox(address), FiniteWaitTimeout.Of(TimeSpan.FromSeconds(2)), CancellationToken.None);
            await _fx.Hub.ReplyAsync(seen.CorrelationId!, new { ok = true }, CancellationToken.None);
        }, TestContext.Current.CancellationToken);

        var response = await _fx.SendRequest.CallAsync(
            new ToolCallRequest("send_request", new Dictionary<string, object?>
            {
                ["target_mailbox"] = address.Raw,
                ["payload"] = JsonNode.Parse("""{ "q": 1 }"""),
                ["timeout_ms"] = 2000,
                ["metadata"] = new Dictionary<string, string> { ["priority"] = "high" },
            }),
            TestContext.Current.CancellationToken);
        await responder;

        Assert.True(response.Success, response.Error);
        Assert.NotNull(seen);
        Assert.Equal("high", seen!.Metadata["priority"]);
        // No id is invented: send_request returns the reply, and nothing claiming to be its
        // correlation id that the responder never saw.
        Assert.False(response.ResultDict().ContainsKey("correlation_id"));
    }
}
