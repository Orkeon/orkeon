using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Application.Tests.EventHub;

public sealed class PostSendReplyTests
{
    private static InMemoryEventHub NewHub(out DefaultEventHubCallerContext caller)
    {
        caller = new DefaultEventHubCallerContext();
        return new InMemoryEventHub(caller, NullLogger<InMemoryEventHub>.Instance);
    }

    [Fact]
    public async System.Threading.Tasks.Task Post_to_unregistered_mailbox_throws_MailboxNotFound()
    {
        using var hub = NewHub(out _);
        var address = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));

        var ex = await Assert.ThrowsAsync<MailboxNotFoundException>(
            () => hub.PostAsync(address, new { hello = "world" }, CancellationToken.None));

        Assert.Equal(address.Raw, ex.MailboxAddress);
    }

    [Fact]
    public async System.Threading.Tasks.Task Send_completes_when_reply_arrives_within_timeout()
    {
        using var hub = NewHub(out _);
        var address = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));
        using var registration = hub.RegisterMailbox(address);

        // Responder: when a request arrives on the mailbox, ReplyAsync with a payload.
        var responderTask = System.Threading.Tasks.Task.Run(async () =>
        {
            var msg = await hub.WaitForAsync(
                new WaitOnMailbox(address),
                FiniteWaitTimeout.Of(TimeSpan.FromSeconds(2)),
                CancellationToken.None);
            Assert.NotNull(msg.CorrelationId);
            await hub.ReplyAsync(msg.CorrelationId!, new { echo = "pong" }, CancellationToken.None);
        }, TestContext.Current.CancellationToken);

        var resp = await hub.SendAsync<object, System.Text.Json.Nodes.JsonNode>(
            address,
            new { ping = true },
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal("pong", resp!["echo"]!.GetValue<string>());
        await responderTask;
    }

    [Fact]
    public async System.Threading.Tasks.Task Send_throws_SendTimeoutException_when_no_reply_in_window()
    {
        using var hub = NewHub(out _);
        var address = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));
        using var registration = hub.RegisterMailbox(address);

        var ex = await Assert.ThrowsAsync<SendTimeoutException>(
            () => hub.SendAsync<object, object>(
                address,
                new { ping = true },
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None));

        Assert.Equal(TimeSpan.FromMilliseconds(100), ex.Timeout);
    }

    [Fact]
    public async System.Threading.Tasks.Task Reply_without_pending_send_throws_UnknownCorrelation()
    {
        using var hub = NewHub(out _);
        var bogus = CorrelationId.NewId();

        var ex = await Assert.ThrowsAsync<UnknownCorrelationException>(
            () => hub.ReplyAsync(bogus, new { value = 1 }, CancellationToken.None));

        Assert.Equal(bogus.AsString(), ex.CorrelationId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Send_throws_when_timeout_is_not_strictly_positive()
    {
        using var hub = NewHub(out _);
        var addr = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));
        using var registration = hub.RegisterMailbox(addr);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => hub.SendAsync<object, object>(addr, new { }, TimeSpan.Zero, CancellationToken.None));
    }
}
