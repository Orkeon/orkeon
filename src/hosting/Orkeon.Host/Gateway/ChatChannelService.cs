using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    /// <summary>Builds the service over the channel configuration and the runner.</summary>
    public ChatChannelService(
        IOptions<DiscordChannelOptions> discord,
        ICrewRunner runner,
        CrewHostRegistry registry,
        ILoggerFactory loggers,
        ILogger<ChatChannelService> logger)
    {
        ArgumentNullException.ThrowIfNull(discord);
        _discord = discord.Value;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_discord.Enabled)
            return;

        if (_registry.Crews.Count == 0)
            return;   // CrewHostService already said so and is stopping the application.

        var authorizer = new AllowListChatAuthorizer(_discord.AllowedUserIds);
        if (authorizer.IsEmpty)
        {
            LogEmptyAllowList();
            return;
        }

        var router = new ThreadIsRunRouter(_registry.Crews[0].Name);
        var gateway = new ChatGateway(_runner, router, authorizer, _registry, _loggers.CreateLogger<ChatGateway>());

        var channel = new DiscordChannel(Options.Create(_discord), _loggers.CreateLogger<DiscordChannel>());
        await using var channelLifetime = channel.ConfigureAwait(false);

        // The stop button and the /stop command reach the same place. A user who prefers
        // clicking should not get a different behaviour from one who prefers typing.
        channel.StopRequested += conversationId =>
        {
            var runId = router.FindRun(conversationId);
            if (runId is not null)
                _registry.RequestStop(runId);

            return Task.CompletedTask;
        };

        await channel.RunAsync(
            (message, ct) => gateway.HandleAsync(message, channel.Responder, ct),
            stoppingToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Discord channel is enabled but its allow list is empty; it would answer nobody, so it will not start. Add Discord:AllowedUserIds.")]
    private partial void LogEmptyAllowList();
}
