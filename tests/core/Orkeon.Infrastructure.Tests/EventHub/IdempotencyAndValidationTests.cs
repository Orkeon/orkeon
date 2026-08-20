using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.EventHub;
using Orkeon.Infrastructure.EventHub.Middleware;

namespace Orkeon.Infrastructure.Tests.EventHub;

/// <summary>
/// The last two stages of the pipeline (HUB-04): idempotency, which refuses what a mailbox
/// already consumed, and validation, which refuses a contract nobody registered.
/// </summary>
public class IdempotencyAndValidationTests
{
    private static Message Message(
        string topic = "t",
        MessageId? id = null,
        MailboxAddress? mailbox = null,
        string? schemaId = null) =>
        new()
        {
            Id = id ?? MessageId.NewId(),
            Topic = topic,
            Payload = ReadOnlyMemory<byte>.Empty,
            SchemaId = schemaId ?? Application.EventHub.Message.NoDeclaredSchemaId,
            PublishedAt = DateTimeOffset.UnixEpoch,
            SourceCrewId = CrewId.Create(),
            TargetMailbox = mailbox,
            Metadata = ImmutableDictionary<string, string>.Empty,
        };

    private static Task<Message> Pass(Message m) => Task.FromResult(m);

    // ── Idempotency ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_mailbox_message_is_delivered_once()
    {
        var stage = new IdempotencyEventHubMiddleware();
        var message = Message(mailbox: MailboxAddress.Parse(new Uri("client://studio")));

        await stage.OnReceiveAsync(message, Pass, CancellationToken.None);

        var duplicate = await Assert.ThrowsAsync<DuplicateMessageException>(
            () => stage.OnReceiveAsync(message, Pass, CancellationToken.None));
        Assert.Equal(message.Id, duplicate.MessageId);
    }

    [Fact]
    public async Task A_topic_message_reaches_every_subscriber()
    {
        // The trap this stage has to avoid: a published message legitimately reaches N
        // subscribers, and deduplicating by identifier would starve all but the first — a bug
        // that would look like a feature.
        var stage = new IdempotencyEventHubMiddleware();
        var message = Message();

        for (var subscriber = 0; subscriber < 3; subscriber++)
            await stage.OnReceiveAsync(message, Pass, CancellationToken.None);
    }

    [Fact]
    public async Task Publishing_the_same_message_twice_is_the_callers_business()
    {
        var stage = new IdempotencyEventHubMiddleware();
        var message = Message(mailbox: MailboxAddress.Parse(new Uri("client://studio")));

        await stage.OnPublishAsync(message, Pass, CancellationToken.None);
        await stage.OnPublishAsync(message, Pass, CancellationToken.None);
    }

    [Fact]
    public async Task The_memory_is_bounded_and_forgets_the_oldest_first()
    {
        var stage = new IdempotencyEventHubMiddleware(capacity: 2);
        var mailbox = MailboxAddress.Parse(new Uri("client://studio"));
        var first = Message(mailbox: mailbox);

        await stage.OnReceiveAsync(first, Pass, CancellationToken.None);
        await stage.OnReceiveAsync(Message(mailbox: mailbox), Pass, CancellationToken.None);
        await stage.OnReceiveAsync(Message(mailbox: mailbox), Pass, CancellationToken.None);

        // The oldest identifier fell out of the window, so its message is no longer known to
        // have been delivered. A bounded memory is a leak that was chosen over a leak that
        // only shows up in production.
        await stage.OnReceiveAsync(first, Pass, CancellationToken.None);
    }

    [Fact]
    public void A_capacity_below_one_remembers_nothing_and_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyEventHubMiddleware(capacity: 0));
    }

    // ── Validation ───────────────────────────────────────────────────────

    private static async Task<IEventSchemaRegistry> RegistryWith(params string[] schemaIds)
    {
        var registry = new InMemoryEventSchemaRegistry();
        foreach (var id in schemaIds)
            await registry.RegisterAsync(id, JsonNode.Parse("""{"type":"object"}""")!, CancellationToken.None);
        return registry;
    }

    [Fact]
    public async Task A_message_declaring_no_contract_passes()
    {
        // Every Post, Send and Reply carries the default identifier. Demanding a registration
        // nobody made would refuse all of A2A.
        var stage = new ValidationEventHubMiddleware(await RegistryWith());

        var message = await stage.OnPublishAsync(Message(), Pass, CancellationToken.None);

        Assert.Equal("t", message.Topic);
    }

    [Fact]
    public async Task A_registered_contract_passes()
    {
        var stage = new ValidationEventHubMiddleware(await RegistryWith("orkeon.run.progress.v1"));

        await stage.OnPublishAsync(
            Message(schemaId: "orkeon.run.progress.v1"), Pass, CancellationToken.None);
    }

    [Fact]
    public async Task A_contract_nobody_registered_is_refused_at_the_source()
    {
        var stage = new ValidationEventHubMiddleware(await RegistryWith("orkeon.run.progress.v1"));

        var ex = await Assert.ThrowsAsync<EventValidationException>(
            () => stage.OnPublishAsync(
                Message(topic: "run.progress", schemaId: "orkeon.run.progres.v1"), Pass, CancellationToken.None));

        Assert.Equal("orkeon.run.progres.v1", ex.SchemaId);
        Assert.Contains("run.progress", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_receive_path_does_not_refuse_what_a_subscriber_cannot_fix()
    {
        var stage = new ValidationEventHubMiddleware(await RegistryWith());

        var message = await stage.OnReceiveAsync(
            Message(schemaId: "never-registered"), Pass, CancellationToken.None);

        Assert.Equal("never-registered", message.SchemaId);
    }
}
