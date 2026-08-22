using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Infrastructure.EventHub;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// BUS-05: the seat an external process gets at the hub. The bridge decorates the in-memory
/// hub — local traffic must keep flowing untouched — serves the peer's <c>client://</c>
/// mailbox so agents can write to it, and projects the peer's own commands back onto the hub.
/// </summary>
public class EventHubBridgeTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public StringWriter Output { get; } = new();
        public DefaultEventHubCallerContext Caller { get; } = new();
        public InMemoryEventHub Inner { get; }
        public JsonLinesEventHubBridge Bridge { get; }

        public Harness(string clientName = "studio")
        {
            Inner = new InMemoryEventHub(Caller, NullLogger<InMemoryEventHub>.Instance);
            Bridge = new JsonLinesEventHubBridge(Inner, new OrkeonEventWriter(Output), clientName, Caller);
        }

        public IReadOnlyList<JsonElement> Emitted() =>
            [.. Output.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            Inner.Dispose();
            Output.Dispose();
        }
    }

    private static MailboxAddress Address(string raw) => MailboxAddress.Parse(new Uri(raw));

    [Fact]
    public async Task An_agent_reaches_the_peer_by_posting_to_its_mailbox()
    {
        await using var harness = new Harness();

        await harness.Bridge.PostAsync(
            Address("client://studio"), new { hello = "world" }, TestContext.Current.CancellationToken);

        var emitted = Assert.Single(harness.Emitted());
        Assert.Equal("hub.message", emitted.GetProperty("kind").GetString());
        // No ambient caller in this harness → no `from`, omitted rather than null. The first
        // version asserted `from == "client://studio"` — the *recipient's* own address — and
        // certified a line that told the peer every message came from itself.
        Assert.False(emitted.TryGetProperty("from", out _));
        Assert.Equal("world", emitted.GetProperty("payload").GetProperty("hello").GetString());
    }

    [Fact]
    public async Task A_different_peer_name_is_a_different_peer_and_stays_local()
    {
        // Not this bridge's client, so it is ordinary local traffic — and the local hub has no
        // such mailbox, which is exactly the error the caller should see.
        await using var harness = new Harness();

        await Assert.ThrowsAsync<MailboxNotFoundException>(
            () => harness.Bridge.PostAsync(
                Address("client://someone-else"), new { }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Local_traffic_goes_through_the_inner_hub_untouched()
    {
        await using var harness = new Harness();

        var subscription = harness.Bridge
            .SubscribeAsync("news", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        try
        {
            await harness.Bridge.PublishAsync(
                "news", new { headline = "x" }, null, TestContext.Current.CancellationToken);

            Assert.True(await subscription.MoveNextAsync());
            Assert.Equal("news", subscription.Current.Topic);
        }
        finally
        {
            await subscription.DisposeAsync();
        }

        // Local traffic is not the peer's business; nothing was relayed outbound.
        Assert.Empty(harness.Emitted());
    }

    [Fact]
    public async Task The_peer_publishes_in_the_agents_own_grammar()
    {
        await using var harness = new Harness();

        var subscription = harness.Inner
            .SubscribeAsync("orders", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        try
        {
            await harness.Bridge.HandleCommandAsync(
                """{"kind":"publish","topic":"orders","payload":{"id":42}}""",
                TestContext.Current.CancellationToken);

            Assert.True(await subscription.MoveNextAsync());
            var payload = JsonSerializer.Deserialize<JsonElement>(subscription.Current.Payload.Span);
            Assert.Equal(42, payload.GetProperty("id").GetInt32());
        }
        finally
        {
            await subscription.DisposeAsync();
        }
    }

    [Fact]
    public async Task Subscribing_relays_a_topic_outbound_until_unsubscribed()
    {
        await using var harness = new Harness();

        await harness.Bridge.HandleCommandAsync(
            """{"kind":"subscribe","topic":"progress"}""", TestContext.Current.CancellationToken);

        await harness.Bridge.PublishAsync(
            "progress", new { percent = 50 }, null, TestContext.Current.CancellationToken);

        var relayed = await WaitForEmissionAsync(harness);
        Assert.Equal("hub.message", relayed.GetProperty("kind").GetString());
        Assert.Equal("progress", relayed.GetProperty("topic").GetString());
        Assert.Equal(50, relayed.GetProperty("payload").GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task Subscribing_twice_to_one_topic_does_not_double_every_message()
    {
        await using var harness = new Harness();

        await harness.Bridge.HandleCommandAsync(
            """{"kind":"subscribe","topic":"progress"}""", TestContext.Current.CancellationToken);
        await harness.Bridge.HandleCommandAsync(
            """{"kind":"subscribe","topic":"progress"}""", TestContext.Current.CancellationToken);

        await harness.Bridge.PublishAsync(
            "progress", new { percent = 50 }, null, TestContext.Current.CancellationToken);

        await WaitForEmissionAsync(harness);
        await Task.Delay(120, TestContext.Current.CancellationToken);

        Assert.Single(harness.Emitted());
    }

    [Fact]
    public async Task An_agent_can_ask_the_peer_a_question_and_read_its_answer()
    {
        await using var harness = new Harness();

        var asking = harness.Bridge.SendAsync<object, JsonElement>(
            Address("client://studio"),
            new { question = "continue?" },
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        var asked = await WaitForEmissionAsync(harness);
        var correlationId = asked.GetProperty("correlationId").GetString();
        Assert.NotNull(correlationId);

        await harness.Bridge.HandleCommandAsync(
            $$$"""{"kind":"reply","correlationId":"{{{correlationId}}}","payload":{"answer":"yes"}}""",
            TestContext.Current.CancellationToken);

        var answer = await asking;
        Assert.Equal("yes", answer.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task A_peer_that_never_answers_times_out_rather_than_hanging()
    {
        await using var harness = new Harness();

        await Assert.ThrowsAsync<SendTimeoutException>(
            () => harness.Bridge.SendAsync<object, JsonElement>(
                Address("client://studio"),
                new { question = "continue?" },
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"kind":"unknown.verb","to":"agent://c/a"}""")]
    [InlineData("""{"kind":"post"}""")]
    [InlineData("""{"kind":"post","to":"not a uri","payload":{}}""")]
    [InlineData("""{"kind":"publish","payload":{}}""")]
    [InlineData("""["not an object"]""")]
    public async Task A_client_sending_nonsense_does_not_stop_the_run(string line)
    {
        // The outbound stream is the contract; the inbound one is tolerant.
        await using var harness = new Harness();

        await harness.Bridge.HandleCommandAsync(line, TestContext.Current.CancellationToken);

        Assert.Empty(harness.Emitted());
    }

    private static async Task<JsonElement> WaitForEmissionAsync(Harness harness)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var emitted = harness.Emitted();
            if (emitted.Count > 0)
                return emitted[0];

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail("No event was emitted on the outbound stream.");
        return default;
    }

    [Fact]
    public async Task A_relayed_post_names_the_crew_that_sent_it()
    {
        // hub.message.from used to carry the *recipient's* own address — every line told the
        // peer it was talking to itself, and nothing could be attributed or answered.
        await using var harness = new Harness();
        var crew = Orkeon.Domain.Common.CrewId.Create();

        using (harness.Caller.Push(new EventHubCaller(crew, null)))
        {
            await harness.Bridge.PostAsync(
                Address("client://studio"), new { hello = "world" }, TestContext.Current.CancellationToken);
        }

        var emitted = Assert.Single(harness.Emitted());
        Assert.Equal($"crew://{crew}", emitted.GetProperty("from").GetString());
    }

    [Fact]
    public async Task A_topic_whose_stream_ended_can_be_subscribed_again()
    {
        // The relay used to keep its slot forever when the hub completed the stream on its
        // own — StartRelay guards on ContainsKey, so the topic became unsubscribable.
        await using var harness = new Harness();

        await harness.Bridge.HandleCommandAsync("""{"kind":"subscribe","topic":"t"}""", TestContext.Current.CancellationToken);
        await harness.Bridge.HandleCommandAsync("""{"kind":"unsubscribe","topic":"t"}""", TestContext.Current.CancellationToken);

        await harness.Bridge.HandleCommandAsync("""{"kind":"subscribe","topic":"t"}""", TestContext.Current.CancellationToken);
        await harness.Inner.PublishAsync("t", new { n = 1 }, null, TestContext.Current.CancellationToken);

        // The republished message must reach the peer through the fresh relay.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && harness.Emitted().Count == 0)
            await Task.Delay(20, TestContext.Current.CancellationToken);

        var emitted = Assert.Single(harness.Emitted());
        Assert.Equal("t", emitted.GetProperty("topic").GetString());
    }
}
