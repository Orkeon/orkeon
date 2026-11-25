using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IHostApplicationLifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Runs the configured chat channels — Discord, Teams, or both — for as long as the service lives (GATE-04, #446).
/// <para>
/// It refuses to start a channel whose allow list is empty. A bot that answers everyone on a
/// server it was invited to spends someone's API budget on strangers, and refusing loudly at
/// startup is the only version of that failure an operator sees before it happens.
/// </para>
/// </summary>
internal sealed partial class ChatChannelService : BackgroundService
{
    private readonly DiscordChannelOptions _discord;
    private readonly TeamsChannelOptions _teams;
    private readonly ICrewRunner _runner;
    private readonly CrewHostRegistry _registry;
    private readonly Files.ConversationFiles? _files;
    private readonly Files.CrewFileFolders? _folders;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<ChatChannelService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly List<Func<ConfiguredChannel>> _channels = [];
    private TeamsChannel? _teamsChannel;
    private static readonly TimeSpan s_suggestionLimit = TimeSpan.FromSeconds(2);

    /// <summary>What builds the Discord channel, a double in the tests.</summary>
    internal Func<IChatChannel> CreateDiscord { get; set; }

    /// <summary>What builds the Teams channel; the tests give it a transport.</summary>
    internal Func<TeamsChannel> CreateTeams { get; set; }

    /// <summary>Builds the service over the channel configuration and the runner.</summary>
    public ChatChannelService(
        IOptions<DiscordChannelOptions> discord,
        IOptions<TeamsChannelOptions> teams,
        ICrewRunner runner,
        CrewHostRegistry registry,
        ILoggerFactory loggers,
        ILogger<ChatChannelService> logger,
        IHostApplicationLifetime lifetime,
        Files.ConversationFiles? files = null,
        Files.CrewFileFolders? folders = null)
    {
        ArgumentNullException.ThrowIfNull(discord);
        _discord = discord.Value;
        ArgumentNullException.ThrowIfNull(teams);
        _teams = teams.Value;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _files = files;
        _folders = folders;
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        CreateDiscord = () => new DiscordChannel(Options.Create(_discord), _loggers.CreateLogger<DiscordChannel>(), fileCommands: _files is not null && _folders is not null);
        CreateTeams = () => new TeamsChannel(Options.Create(_teams), _loggers.CreateLogger<TeamsChannel>());
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Validated in StartAsync, deliberately: a refused channel configuration fails the
        // host start itself, before READY=1. The first version logged and returned — the
        // daemon stayed up, deaf, and reported as started, the one version of the failure
        // no operator sees before it matters.
        _channels.Clear();
        var routed = new List<(string Channel, ChatRoutes Routes)>();

        if (_discord.Enabled)
        {
            ValidateDiscordConfiguration();
            var routes = ChatRoutes.From(_discord, _registry.Crews);
            routed.Add(("Discord", routes));
            _channels.Add(() => new ConfiguredChannel(
                "Discord",
                CreateDiscord(),
                new AllowListChatAuthorizer(_discord.AllowedUserIds),
                routes));
        }

        if (_teams.Enabled)
        {
            // The routes first: without a crew nothing else is worth saying, as for Discord.
            var routes = ChatRoutes.From(_teams, _registry.Crews);
            if (_teams.AllowedUserIds.Count == 0)
                LogEmptyAllowList("Teams");
            _teams.Validate();
            routed.Add(("Teams", routes));

            // Bound here, so a port already held refuses the start instead of crashing a host that reported ready.
            _teamsChannel = CreateTeams();
            _teamsChannel.Bind();
            _channels.Add(() => new ConfiguredChannel(
                "Teams",
                _teamsChannel,
                new AllowListChatAuthorizer(_teams.AllowedUserIds.Select(id => Guid.Parse(id).ToString("D"))),
                routes));
        }

        // Which crew each room reaches (GAP-11), refused above when a route cannot be honoured. A crew no
        // room of any channel reaches is named, so hosting it is a choice the operator sees rather than
        // the silent limit it used to be.
        foreach (var (channel, routes) in routed)
        {
            foreach (var crew in routes.UnreachableCrews(_registry.Crews))
            {
                if (routed.All(r => r.Routes.UnreachableCrews(_registry.Crews).Contains(crew)))
                    LogUnreachableCrew(crew, channel);
            }
        }

        return base.StartAsync(cancellationToken);
    }

    /// <summary>The refusals of an enabled Discord channel, each a sentence naming the key to fix.</summary>
    private void ValidateDiscordConfiguration()
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
            LogEmptyAllowList("Discord");
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

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_channels.Select(build => RunChannelAsync(build(), stoppingToken)));

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        _teamsChannel?.Dispose();
    }

    /// <summary>A channel with its own allow list and routes: a Discord id means nothing to Teams.</summary>
    private sealed record ConfiguredChannel(string Name, IChatChannel Channel, IChatAuthorizer Authorizer, ChatRoutes Routes);

    private async Task RunChannelAsync(ConfiguredChannel configured, CancellationToken stoppingToken)
    {
        var channel = configured.Channel;
        var authorizer = configured.Authorizer;
        await using var channelLifetime = ((IAsyncDisposable)channel).ConfigureAwait(false);
        var gateway = new ChatGateway(
            _runner, new ThreadIsRunRouter(configured.Routes), authorizer, _registry, _loggers.CreateLogger<ChatGateway>(), _files);

        var commands = _files is null || _folders is null
            ? null
            : new ChatFileCommands(channel.Name, configured.Routes, _registry.Crews, _files, _folders);

        // The stop button and the /stop command arrive as ONE invocation shape, and the
        // allow list gates both: the first version checked it on the typed command only,
        // and anyone who could see the thread could kill the run with a click.
        channel.CommandInvoked += invocation =>
            HandleCommandAsync(gateway, authorizer, invocation, stoppingToken, commands);
        if (channel is ISuggestingChannel suggesting)
            suggesting.SuggestionsAsked += invocation => SuggestAsync(authorizer, invocation, commands, stoppingToken);

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
            LogChannelRefusedConfiguration(ex, configured.Name);
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
            LogChannelCrashed(ex, configured.Name);
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
        }
    }

    /// <summary>
    /// One registered command, end to end: authorize, then act, then word the answer. The
    /// reply goes back ephemerally to the invoker alone.
    /// </summary>
    internal static async Task<CommandReply> HandleCommandAsync(
        ChatGateway gateway, IChatAuthorizer authorizer, CommandInvocation invocation, CancellationToken ct, ChatFileCommands? files = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(invocation);

        if (!authorizer.IsAuthorized(invocation.SenderId))
            return new CommandReply("You are not authorized to use this bot.");

        // Their names are registered only when files are on; a client that still has them is told.
        if (invocation.CommandName is ChatCommands.Files or ChatCommands.Get or ChatCommands.Put)
        {
            return files is null
                ? new CommandReply("The file commands are off on this host.")
                : await files.AnswerAsync(invocation, ct).ConfigureAwait(false);
        }

        return new CommandReply(invocation.CommandName switch
        {
            ChatCommands.Stop => gateway.Stop(invocation.ConversationId),
            ChatCommands.Status => gateway.Status(invocation.ConversationId),
            _ => "Unknown command.",
        });
    }

    /// <summary>
    /// What may be typed in a command: nothing for who is not allowed, before anything is looked up, and nothing when it
    /// cannot be found in the time a platform waits.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> SuggestAsync(
        IChatAuthorizer authorizer, CommandInvocation invocation, ChatFileCommands? files, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(invocation);
        if (files is null || !authorizer.IsAuthorized(invocation.SenderId))
            return [];

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(s_suggestionLimit);
        try
        {
            return await files.SuggestAsync(invocation, bounded.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A suggestion that cannot be made is none: the command itself says what is wrong.
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The {Channel} channel is enabled but its allow list is empty; it would answer nobody, so it will not start. Add {Channel}:AllowedUserIds.")]
    private partial void LogEmptyAllowList(string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hosted crew '{CrewName}' is reached by no {Channel} room: it is neither {Channel}:DefaultCrew nor the target of a {Channel}:Routes entry")]
    private partial void LogUnreachableCrew(string crewName, string channel);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The {Channel} channel died; stopping the host so the supervisor restarts it")]
    private partial void LogChannelCrashed(Exception ex, string channel);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The {Channel} channel refused its configuration; stopping the host without a restart loop")]
    private partial void LogChannelRefusedConfiguration(Exception ex, string channel);
}
