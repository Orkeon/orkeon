using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// The channel's start-time refusals (GATE-04). Each one used to log and return, leaving the
/// daemon up, deaf, and reported as started — the one version of the failure no operator
/// sees before it matters. They now fail the host START, before READY=1, with exit 78.
/// </summary>
public sealed class ChatChannelServiceTests
{
    private sealed class NoopLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication()
        {
            // Recorded nowhere: these tests only exercise StartAsync.
        }
    }

    private static ChatChannelService Build(DiscordChannelOptions discord, params HostedCrewOptions[] crews)
    {
        var registry = new CrewHostRegistry(Options.Create(new OrkeonHostOptions { Crews = crews }));
        return new ChatChannelService(
            Options.Create(discord),
            new ScriptedRunner(),
            registry,
            NullLoggerFactory.Instance,
            NullLogger<ChatChannelService>.Instance,
            new NoopLifetime());
    }

    private static HostedCrewOptions Crew() => new() { Name = "support", Path = "/crews/support" };

    private static DiscordChannelOptions Enabled() => new()
    {
        Enabled = true,
        AllowedUserIds = ["123"],
        TokenEnvironmentVariable = $"ORKEON_TEST_TOKEN_{Guid.NewGuid():N}",
    };

    [Fact]
    public async Task A_disabled_channel_starts_without_complaint()
    {
        using var service = Build(new DiscordChannelOptions { Enabled = false });

        await service.StartAsync(TestContext.Current.CancellationToken);

        // Nothing was demanded on the way in (no crew, no allow list, no token — each of
        // which refuses the start once the channel is enabled), and the background loop
        // ends on its own: a disabled channel listens to nothing. Awaiting the loop rather
        // than probing its status is what makes the second half a real check -- a fault
        // inside ExecuteAsync surfaces here instead of being swallowed as "not completed".
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_enabled_channel_with_no_crew_refuses_the_start()
    {
        // Validated here, not borrowed from CrewHostService: this service now starts first
        // (registration order is stop order reversed — the drain must precede the channel's
        // stop), so it can no longer lean on the host service having refused already.
        using var service = Build(Enabled(), crews: []);

        await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_empty_allow_list_refuses_the_start()
    {
        var options = Enabled();
        options = options with { AllowedUserIds = [] };
        using var service = Build(options, Crew());

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("AllowedUserIds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unset_token_variable_refuses_the_start()
    {
        // The variable is NAMED in configuration and read from the environment; an unset one
        // used to be discovered at login time, with the daemon already "ready".
        using var service = Build(Enabled(), Crew());

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("ORKEON_TEST_TOKEN_", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_zero_progress_interval_refuses_the_start()
    {
        var options = Enabled() with { ProgressInterval = TimeSpan.Zero };
        using var service = Build(options, Crew());

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("ProgressInterval", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guild_id_that_is_not_a_number_refuses_the_start()
    {
        // Discovered at start (exit 78), not at Ready — where registration would just log a
        // warning forever while the operator waits for commands that never appear.
        var options = Enabled() with { GuildIds = ["not-a-guild"] };
        Environment.SetEnvironmentVariable(options.TokenEnvironmentVariable, "a-token");
        try
        {
            using var service = Build(options, Crew());

            var ex = await Assert.ThrowsAsync<HostConfigurationException>(
                () => service.StartAsync(TestContext.Current.CancellationToken));
            Assert.Contains("not-a-guild", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(options.TokenEnvironmentVariable, null);
        }
    }

    // ---- One registered command, end to end (the wiring the channel invokes) ------------

    private static (ChatGateway Gateway, AllowListChatAuthorizer Authorizer, CrewHostRegistry Registry, ThreadIsRunRouter Router) Wiring()
    {
        var registry = new CrewHostRegistry(Options.Create(new OrkeonHostOptions { Crews = [Crew()] }));
        var router = new ThreadIsRunRouter("support");
        var gateway = new ChatGateway(
            new ScriptedRunner(), router, new AllowListChatAuthorizer(["trusted"]), registry,
            NullLogger<ChatGateway>.Instance);

        return (gateway, new AllowListChatAuthorizer(["trusted"]), registry, router);
    }

    [Fact]
    public void A_command_from_a_stranger_is_refused_before_it_acts()
    {
        // The gap this pins closed: the stop BUTTON used to skip the allow list entirely —
        // anyone who could see the thread could kill the run, while typing /stop was gated.
        var (gateway, authorizer, registry, router) = Wiring();
        var run = registry.TryStart("support", "discord:thread-1")!;
        router.Attach("thread-1", run.Id);

        var text = ChatChannelService.HandleCommand(
            gateway, authorizer, new CommandInvocation("stop", "thread-1", "stranger"));

        Assert.Contains("not authorized", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(run.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Stop_from_an_allowed_sender_reaches_the_conversations_run()
    {
        var (gateway, authorizer, registry, router) = Wiring();
        var run = registry.TryStart("support", "discord:thread-1")!;
        router.Attach("thread-1", run.Id);

        var text = ChatChannelService.HandleCommand(
            gateway, authorizer, new CommandInvocation("stop", "thread-1", "trusted"));

        Assert.Equal("Stopping.", text);
        Assert.True(run.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Status_from_an_allowed_sender_reports_the_conversation()
    {
        var (gateway, authorizer, registry, router) = Wiring();
        var run = registry.TryStart("support", "discord:thread-1")!;
        router.Attach("thread-1", run.Id);

        var text = ChatChannelService.HandleCommand(
            gateway, authorizer, new CommandInvocation("status", "thread-1", "trusted"));

        Assert.Contains("support", text, StringComparison.Ordinal);
        Assert.Contains(" for ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_command_name_is_answered_not_dropped()
    {
        // Registration and dispatch could drift (an old client, a stale global cache); the
        // invoker deserves words, not the "this interaction failed" banner.
        var (gateway, authorizer, _, _) = Wiring();

        var text = ChatChannelService.HandleCommand(
            gateway, authorizer, new CommandInvocation("restart", "thread-1", "trusted"));

        Assert.Equal("Unknown command.", text);
    }
}
