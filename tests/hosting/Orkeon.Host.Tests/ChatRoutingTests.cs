using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-11 — the chat reaches every hosted crew: a Discord room (the channel a thread is opened
/// in) is routed to a crew by <c>Orkeon:Host:Discord:Routes</c>, and a room without a route
/// goes to the default crew — <c>DefaultCrew</c>, or the first declared. Until this, every
/// message went to the first crew, and the others were hosted, bounded and unreachable. A route
/// the host cannot honour is refused at startup.
/// </summary>
public sealed class ChatRoutingTests
{
    private static readonly HostedCrewOptions Triage = new() { Name = "triage", Path = "/crews/triage.yaml" };
    private static readonly HostedCrewOptions Billing = new() { Name = "billing", Path = "/crews/billing.yaml" };

    private static InboundMessage Message(string room, string conversation = "thread-1") => new()
    {
        Channel = "test",
        RoomId = room,
        ConversationId = conversation,
        SenderId = "trusted",
        Text = "hello",
        ReceivedAt = DateTimeOffset.UnixEpoch,
    };

    private static DiscordChannelOptions Discord(Dictionary<string, string>? routes = null, string? defaultCrew = null) => new()
    {
        Enabled = true,
        AllowedUserIds = ["trusted"],
        Routes = routes ?? [],
        DefaultCrew = defaultCrew,
    };

    [Fact]
    public async Task A_routed_room_starts_its_crew_and_an_unrouted_room_the_default()
    {
        var crews = new[] { Triage, Billing };
        var routes = ChatRoutes.From(Discord(new() { ["222"] = "billing" }), crews);
        var registry = new CrewHostRegistry(Options.Create(new OrkeonHostOptions { Crews = crews }));
        var runner = new ScriptedRunner();
        var gateway = new ChatGateway(
            runner, new ThreadIsRunRouter(routes), new AllowListChatAuthorizer(["trusted"]), registry,
            NullLogger<ChatGateway>.Instance);

        await gateway.HandleAsync(Message("222", "thread-a"), new RecordingResponder(), TestContext.Current.CancellationToken);
        await gateway.HandleAsync(Message("111", "thread-b"), new RecordingResponder(), TestContext.Current.CancellationToken);

        Assert.Equal(["billing:hello", "triage:hello"], runner.Ran);
    }

    [Fact]
    public void DefaultCrew_names_the_crew_unrouted_rooms_reach()
    {
        var routes = ChatRoutes.From(Discord(defaultCrew: "Billing"), [Triage, Billing]);

        Assert.Equal("billing", routes.Resolve(Message("999")));
    }

    [Fact]
    public void Without_DefaultCrew_the_first_declared_crew_is_the_default()
    {
        var routes = ChatRoutes.From(Discord(), [Triage, Billing]);

        Assert.Equal("triage", routes.Resolve(Message("999")));
    }

    [Fact]
    public void A_crew_neither_routed_nor_default_is_named_as_unreachable()
    {
        var routes = ChatRoutes.From(Discord(), [Triage, Billing]);

        Assert.Equal(["billing"], routes.UnreachableCrews([Triage, Billing]));
    }

    [Fact]
    public void A_route_to_an_unknown_crew_is_refused()
    {
        var ex = Assert.Throws<HostConfigurationException>(
            () => ChatRoutes.From(Discord(new() { ["222"] = "refunds" }), [Triage, Billing]));

        Assert.Contains("refunds", ex.Message, StringComparison.Ordinal);
        Assert.Contains("222", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_route_key_that_is_not_a_channel_id_is_refused()
    {
        var ex = Assert.Throws<HostConfigurationException>(
            () => ChatRoutes.From(Discord(new() { ["#billing"] = "billing" }), [Triage, Billing]));

        Assert.Contains("#billing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_DefaultCrew_is_refused()
    {
        var ex = Assert.Throws<HostConfigurationException>(
            () => ChatRoutes.From(Discord(defaultCrew: "refunds"), [Triage, Billing]));

        Assert.Contains("refunds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_channel_refuses_to_start_on_a_route_to_an_unknown_crew()
    {
        var options = Discord(new() { ["222"] = "refunds" }) with
        {
            TokenEnvironmentVariable = $"ORKEON_TEST_TOKEN_{Guid.NewGuid():N}",
        };
        Environment.SetEnvironmentVariable(options.TokenEnvironmentVariable, "a-token");
        try
        {
            using var service = new ChatChannelService(
                Options.Create(options),
                new ScriptedRunner(),
                new CrewHostRegistry(Options.Create(new OrkeonHostOptions { Crews = [Triage, Billing] })),
                NullLoggerFactory.Instance,
                NullLogger<ChatChannelService>.Instance,
                new NoopLifetime());

            var ex = await Assert.ThrowsAsync<HostConfigurationException>(
                () => service.StartAsync(TestContext.Current.CancellationToken));
            Assert.Contains("refunds", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(options.TokenEnvironmentVariable, null);
        }
    }

    [Fact]
    public void A_thread_of_a_routed_room_keeps_one_run_per_thread()
    {
        // The route picks the crew; the thread is still the run. Two threads of one room are
        // two runs of its crew, and a second message in a busy thread is refused as before.
        var router = new ThreadIsRunRouter(ChatRoutes.From(Discord(new() { ["222"] = "billing" }), [Triage, Billing]));

        Assert.True(router.TryBegin("thread-a"));
        Assert.False(router.TryBegin("thread-a"));
        Assert.True(router.TryBegin("thread-b"));
        Assert.Equal("billing", router.ResolveCrew(Message("222", "thread-b")));
    }

    private sealed class NoopLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
