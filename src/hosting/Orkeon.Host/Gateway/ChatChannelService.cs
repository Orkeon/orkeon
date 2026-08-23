using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IHostApplicationLifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Runs the configured chat channels for as long as the service lives (GATE-04).
/// <para>
/// It refuses to start a channel whose allow list is empty. A bot that answers everyone on a
/// server it was invited to spends someone's API budget on strangers, and refusing loudly at
/// startup is the only version of that failure an operator sees before it happens.
/// </para>
/// </summary>
internal sealed partial class ChatChannelService : BackgroundService
{
    private readonly DiscordChannelOptions _discord;
    private readonly ICrewRunner _runner;
    private readonly CrewHostRegistry _registry;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<ChatChannelService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Builds the service over the channel configuration and the runner.</summary>
    public ChatChannelService(
        IOptions<DiscordChannelOptions> discord,
        ICrewRunner runner,
        CrewHostRegistry registry,
        ILoggerFactory loggers,
        ILogger<ChatChannelService> logger,
        IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(discord);
        _discord = discord.Value;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Validated in StartAsync, deliberately: a refused channel configuration fails the
        // host start itself, before READY=1. The first version logged and returned — the
        // daemon stayed up, deaf, and reported as started, the one version of the failure
        // no operator sees before it matters.
        if (_discord.Enabled)
        {
            // Validated here rather than assumed: this service now STARTS before
            // CrewHostService (registration order is stop order reversed, and the drain must
            // precede the channel's stop), so it can no longer lean on the host service
            // having refused an empty crew list first.
            if (_registry.Crews.Count == 0)
                throw new HostConfigurationException(
                    $"The Discord channel is enabled but no crew is configured under '{OrkeonHostOptions.SectionName}:Crews'.");

            if (_discord.AllowedUserIds.Count == 0)
            {
                LogEmptyAllowList();
                throw new HostConfigurationException(
                    "The Discord channel is enabled but Discord:AllowedUserIds is empty — it would answer nobody.");
            }

            if (_discord.ProgressInterval <= TimeSpan.Zero)
                throw new HostConfigurationException(
                    $"Discord:ProgressInterval must be positive; got {_discord.ProgressInterval}.");

            if (_discord.ReadToken() is null)
                throw new HostConfigurationException(
                    $"The Discord channel is enabled but the environment variable "
                    + $"'{_discord.TokenEnvironmentVariable}' is empty or unset.");

            foreach (var guildId in _discord.GuildIds)
            {
                if (!ulong.TryParse(guildId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new HostConfigurationException(
                        $"Discord:GuildIds contains '{guildId}', which is not a Discord guild id (a number).");
            }
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_discord.Enabled)
            return;

        var authorizer = new AllowListChatAuthorizer(_discord.AllowedUserIds);
        var router = new ThreadIsRunRouter(_registry.Crews[0].Name);
        var gateway = new ChatGateway(_runner, router, authorizer, _registry, _loggers.CreateLogger<ChatGateway>());

        var channel = new DiscordChannel(Options.Create(_discord), _loggers.CreateLogger<DiscordChannel>());
        await using var channelLifetime = channel.ConfigureAwait(false);

        // The stop button and the /stop command arrive as ONE invocation shape, and the
        // allow list gates both: the first version checked it on the typed command only,
        // and anyone who could see the thread could kill the run with a click.
        channel.CommandInvoked += invocation =>
            Task.FromResult(HandleCommand(gateway, authorizer, invocation));

        try
        {
            await channel.RunAsync(
                (message, ct) => gateway.HandleAsync(message, channel.Responder, ct),
                stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected: this is how the host stops us.
        }
        catch (HostConfigurationException ex)
        {
            // A configuration refusal keeps its contract (exit 78, no restart loop) even if
            // one slips past StartAsync. Rethrowing would NOT keep it: an ExecuteAsync
            // escape goes to BackgroundServiceExceptionBehavior.StopHost, which exits 0 —
            // and mapping it to the crash path would put a typo on a ten-second restart
            // cycle instead.
            LogChannelRefusedConfiguration(ex);
            Environment.ExitCode = HostConfigurationException.ExitCode;
            _lifetime.StopApplication();
        }
#pragma warning disable CA1031 // Fault barrier for the channel: the failure mode is chosen here, not propagated blind.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // A dead channel is a dead service — the daemon exists to be reachable. Exit
            // non-zero so Restart=on-failure actually restarts: the default behaviour
            // (StopHost) exits 0, and systemd read a crashed bot as a clean, deliberate stop
            // that it must respect forever.
            LogChannelCrashed(ex);
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
        }
    }

    /// <summary>
    /// One registered command, end to end: authorize, then act, then word the answer. The
    /// text goes back ephemerally to the invoker alone.
    /// </summary>
    internal static string HandleCommand(ChatGateway gateway, IChatAuthorizer authorizer, CommandInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(invocation);

        if (!authorizer.IsAuthorized(invocation.SenderId))
            return "You are not authorized to use this bot.";

        return invocation.CommandName switch
        {
            DiscordChannel.StopCommandName => gateway.Stop(invocation.ConversationId),
            DiscordChannel.StatusCommandName => gateway.Status(invocation.ConversationId),
            _ => "Unknown command.",
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Discord channel is enabled but its allow list is empty; it would answer nobody, so it will not start. Add Discord:AllowedUserIds.")]
    private partial void LogEmptyAllowList();

    [LoggerMessage(Level = LogLevel.Critical, Message = "The Discord channel died; stopping the host so the supervisor restarts it")]
    private partial void LogChannelCrashed(Exception ex);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The Discord channel refused its configuration; stopping the host without a restart loop")]
    private partial void LogChannelRefusedConfiguration(Exception ex);
}
