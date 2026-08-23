using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The Discord adapter (GATE-04): the first place people talk to Orkeon from without a terminal.
/// <para>
/// A thread is a run (decision G2). It is the only mapping a user can predict without being
/// told — what happens in this thread is one job — and it gives parallelism without inventing a
/// notion of session anyone has to learn.
/// </para>
/// <para>
/// The adapter is deliberately thin. Authorization, routing, the acknowledgement and the
/// throttling all live in the gateway and the responder, so this class only translates: a
/// Discord message becomes an <see cref="InboundMessage"/>, a reply becomes a Discord message.
/// Anything smarter here would have to be written again for the next platform.
/// </para>
/// </summary>
internal sealed partial class DiscordChannel : IChatChannel, IAsyncDisposable
{
    /// <summary>The custom id carried by the stop button, and read back when it is pressed.</summary>
    public const string StopButtonId = "orkeon-stop";

    private readonly DiscordChannelOptions _options;
    private readonly ILogger<DiscordChannel> _logger;
    private readonly DiscordSocketClient _client;
    private readonly TimeProvider _time;

    /// <summary>Builds the channel over its configuration.</summary>
    public DiscordChannel(
        IOptions<DiscordChannelOptions> options,
        ILogger<DiscordChannel> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            // MessageContent is privileged and must be enabled on the application too. Without
            // it every message arrives with an empty body, which looks like a broken bot rather
            // than a missing permission — so the host says so at startup instead.
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Warning,
        });

        Responder = new ThrottledResponder(
            new DiscordResponder(_client, logger),
            _options.ProgressInterval,
            _time);
    }

    /// <inheritdoc />
    public string Name => "discord";

    /// <inheritdoc />
    public IChatResponder Responder { get; }

    /// <summary>
    /// Raised for a slash command or the stop button; the returned text is shown only to the
    /// person who invoked it. One event for both on purpose: the button is `/stop` with a
    /// different finger, and two paths would drift — the first version proved it by checking
    /// the allow list on the typed command and not on the click.
    /// </summary>
    public event Func<CommandInvocation, Task<string>>? CommandInvoked;

    /// <inheritdoc />
    public async Task RunAsync(Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        var token = _options.ReadToken();
        if (token is null)
        {
            // Naming the variable is the whole point: "token missing" without it sends an
            // operator hunting through three files. And it throws rather than returning: a
            // silent return left the daemon up, deaf, and reported as healthy — the one
            // version of this failure no operator ever sees.
            LogNoToken(_options.TokenEnvironmentVariable);
            throw new HostConfigurationException(
                $"The Discord channel is enabled but the environment variable "
                + $"'{_options.TokenEnvironmentVariable}' is empty or unset.");
        }

        _client.Log += message =>
        {
            LogFromDiscord(message.Source, message.Message ?? string.Empty);
            return Task.CompletedTask;
        };

        // Handlers are dispatched OFF the gateway task, deliberately. Discord.Net awaits
        // each handler on the connection's dispatch loop: a handler that runs the whole crew
        // (minutes) blocks /stop, the stop button, every concurrent thread — and the
        // HeartbeatAck frames, so any run longer than the heartbeat window forced a
        // disconnect. That serialization is also what made MaxConcurrentRuns unreachable
        // from Discord.
        _client.MessageReceived += socketMessage =>
        {
            Dispatch(() => OnMessageAsync(socketMessage, onMessage, ct));
            return Task.CompletedTask;
        };
        _client.ButtonExecuted += component =>
        {
            Dispatch(() => OnButtonAsync(component));
            return Task.CompletedTask;
        };
        _client.SlashCommandExecuted += command =>
        {
            Dispatch(() => OnSlashCommandAsync(command));
            return Task.CompletedTask;
        };

        // Registration needs the application id, which is only known once the gateway says
        // Ready — and Ready fires again on every reconnect, which is harmless because a bulk
        // overwrite is idempotent.
        _client.Ready += () =>
        {
            Dispatch(RegisterCommandsAsync);
            return Task.CompletedTask;
        };

        await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);
        LogConnected();

        try
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: this is how the host stops us.
        }

        await _client.StopAsync().ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fault barrier for detached handlers: an exception here has no caller left to reach, so it is logged instead of lost.")]
    private void Dispatch(Func<Task> handler)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogHandlerFailed(ex);
            }
        });
    }

    private async Task OnMessageAsync(
        SocketMessage socketMessage,
        Func<InboundMessage, CancellationToken, Task> onMessage,
        CancellationToken ct)
    {
        // A bot answering itself is an infinite loop with a rate limit at the end of it.
        if (socketMessage.Author.IsBot)
            return;

        // A thread is a run. A message in a plain channel has no run to belong to, so it is
        // ignored rather than silently starting one somewhere the user cannot follow.
        if (socketMessage.Channel is not SocketThreadChannel thread)
            return;

        var message = ToInbound(socketMessage, thread, _time.GetUtcNow());
        await onMessage(message, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates a Discord message into the gateway's own shape. Static and separate so the
    /// mapping is testable without a socket, a token or a server.
    /// </summary>
    internal static InboundMessage ToInbound(IMessage message, IChannel thread, DateTimeOffset receivedAt) =>
        new()
        {
            Channel = "discord",
            ConversationId = thread.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SenderId = message.Author.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Text = message.Content ?? string.Empty,
            ReceivedAt = receivedAt,
        };

    private async Task OnButtonAsync(SocketMessageComponent component)
    {
        if (!string.Equals(component.Data.CustomId, StopButtonId, StringComparison.Ordinal))
            return;

        // The button is /stop with a different finger: same invocation, same authorization,
        // same wording back. The first version deferred silently and skipped the allow list —
        // anyone who could see the thread could kill the run, while typing /stop was gated.
        await RespondToInvocationAsync(
            ToInvocation(StopCommandName, component.ChannelId, component.User.Id),
            text => component.RespondAsync(text, ephemeral: true)).ConfigureAwait(false);
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        // ChannelId over Channel: the interaction payload always carries the id, while the
        // Channel object is null for an uncached channel — a fresh thread, typically.
        await RespondToInvocationAsync(
            ToInvocation(command.CommandName, command.ChannelId, command.User.Id),
            text => command.RespondAsync(text, ephemeral: true)).ConfigureAwait(false);
    }

    private async Task RespondToInvocationAsync(CommandInvocation? invocation, Func<string, Task> respond)
    {
        if (invocation is null)
            return;

        // Discord closes the interaction in three seconds; both commands are registry
        // lookups, so responding directly (no Defer) stays well inside the window. The
        // response is ephemeral: a status poke or a refusal is the invoker's business, not
        // one more line in everyone's thread.
        var text = CommandInvoked is { } handler
            ? await handler(invocation).ConfigureAwait(false)
            : "The host is not listening to commands.";

        await respond(text).ConfigureAwait(false);
    }

    /// <summary>The name of the registered stop command — the button reuses it.</summary>
    internal const string StopCommandName = "stop";

    /// <summary>The name of the registered status command.</summary>
    internal const string StatusCommandName = "status";

    /// <summary>
    /// The slash commands this channel registers. Static and separate so a test can hold the
    /// registered names to the ones the executed-command handler reads back — a mismatch here
    /// is a command that does nothing.
    /// </summary>
    internal static SlashCommandProperties[] SlashCommands() =>
    [
        new SlashCommandBuilder()
            .WithName(StatusCommandName)
            .WithDescription("What this conversation is running, and since when.")
            .Build(),
        new SlashCommandBuilder()
            .WithName(StopCommandName)
            .WithDescription("Stops this conversation's run.")
            .Build(),
    ];

    /// <summary>
    /// Translates an interaction into the gateway's shape, on primitives because the socket
    /// interaction types cannot be built in a test. A null channel id has no conversation to
    /// act on, so the invocation is refused rather than guessed.
    /// </summary>
    internal static CommandInvocation? ToInvocation(string commandName, ulong? channelId, ulong userId) =>
        channelId is { } id
            ? new CommandInvocation(
                commandName,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                userId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : null;

    private async Task RegisterCommandsAsync()
    {
        try
        {
            // Guild registration propagates immediately (the dev loop); global registration
            // needs no configuration but is cached by Discord for up to an hour.
            if (_options.GuildIds.Count > 0)
            {
                foreach (var guildId in _options.GuildIds)
                {
                    await _client.Rest.BulkOverwriteGuildCommands(
                        SlashCommands(),
                        ulong.Parse(guildId, System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
                }
            }
            else
            {
                await _client.BulkOverwriteGlobalApplicationCommandsAsync(SlashCommands()).ConfigureAwait(false);
            }

            LogCommandsRegistered(_options.GuildIds.Count);
        }
#pragma warning disable CA1031 // A registration hiccup must not kill the daemon: the message channel still works.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCommandRegistrationFailed(ex);
        }
    }

    /// <summary>The message component carrying the stop button.</summary>
    internal static MessageComponent StopButton() =>
        new ComponentBuilder()
            .WithButton("Stop", StopButtonId, ButtonStyle.Danger)
            .Build();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Discord channel is enabled but ${TokenVariable} is not set; the channel will not start.")]
    private partial void LogNoToken(string tokenVariable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discord channel connected")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord [{Source}] {Message}")]
    private partial void LogFromDiscord(string source, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "A Discord handler failed; the message it served gets no reply")]
    private partial void LogHandlerFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Slash commands registered ({GuildCount} guild(s); 0 means global)")]
    private partial void LogCommandsRegistered(int guildCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slash command registration failed; /status and /stop will be unavailable until the next reconnect, the message channel still works")]
    private partial void LogCommandRegistrationFailed(Exception ex);
}

/// <summary>
/// Sends the gateway's replies to a Discord thread.
/// <para>
/// The acknowledgement carries the stop button, so a person can interrupt a run from the moment
/// it starts rather than from the moment it first reports progress — which on a slow first step
/// can be minutes later.
/// </para>
/// </summary>
internal sealed partial class DiscordResponder : IChatResponder
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger _logger;

    /// <summary>Builds the responder over the connected client.</summary>
    public DiscordResponder(DiscordSocketClient client, ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task AcknowledgeAsync(InboundMessage message, string text, CancellationToken ct) =>
        SendAsync(message, text, DiscordChannel.StopButton());

    /// <inheritdoc />
    public Task ProgressAsync(InboundMessage message, string text, CancellationToken ct) =>
        SendAsync(message, text, components: null);

    /// <inheritdoc />
    public Task CompleteAsync(InboundMessage message, string text, CancellationToken ct) =>
        SendAsync(message, Truncate(text), components: null);

    private async Task SendAsync(InboundMessage message, string text, MessageComponent? components)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!ulong.TryParse(message.ConversationId, out var channelId))
        {
            LogNoChannel(message.ConversationId);
            return;
        }

        try
        {
            // The lookup sits INSIDE the barrier: GetChannelAsync falls back to REST for an
            // uncached channel and can throw on a hiccup — and the acknowledgement is awaited
            // by the runner, so a throw here used to fail a run over a greeting.
            if (await _client.GetChannelAsync(channelId).ConfigureAwait(false) is not IMessageChannel channel)
            {
                LogNoChannel(message.ConversationId);
                return;
            }

            await channel.SendMessageAsync(Truncate(text), components: components).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A channel that refuses a reply must not fail the run.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(ex, message.ConversationId);
        }
    }

    /// <summary>
    /// Discord refuses a message over 2000 characters. A crew's answer regularly exceeds it, so
    /// it is cut rather than rejected — losing the tail beats losing the whole answer.
    /// </summary>
    internal static string Truncate(string text)
    {
        const int limit = 2000;
        const string ellipsis = "\n…(truncated)";

        // Whitespace-only counts as empty: Discord refuses a blank body, so the send would
        // fail and the user's only reading of "it finished" would be total silence.
        if (string.IsNullOrWhiteSpace(text))
            return "(no output)";

        if (text.Length <= limit)
            return text;

        // Never cut inside a surrogate pair: an emoji at the boundary would leave a lone
        // surrogate, which Discord rejects — turning "too long" into "not sent at all".
        var cut = limit - ellipsis.Length;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;

        return text[..cut] + ellipsis;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Discord channel {ConversationId} to reply in")]
    private partial void LogNoChannel(string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send a Discord message to {ConversationId}")]
    private partial void LogSendFailed(Exception ex, string conversationId);
}
