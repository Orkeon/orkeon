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
}
