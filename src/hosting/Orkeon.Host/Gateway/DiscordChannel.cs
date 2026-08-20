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

    /// <summary>Raised when someone presses the stop button, with the thread it was pressed in.</summary>
    public event Func<string, Task>? StopRequested;

    /// <inheritdoc />
    public async Task RunAsync(Func<InboundMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        var token = _options.ReadToken();
        if (token is null)
        {
            // Naming the variable is the whole point: "token missing" without it sends an
            // operator hunting through three files.
            LogNoToken(_options.TokenEnvironmentVariable);
            return;
        }

        _client.Log += message =>
        {
            LogFromDiscord(message.Source, message.Message ?? string.Empty);
            return Task.CompletedTask;
        };

        _client.MessageReceived += socketMessage => OnMessageAsync(socketMessage, onMessage, ct);
        _client.ButtonExecuted += OnButtonAsync;

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

        // Discord closes the interaction in three seconds. Deferring first is what keeps the
        // button from showing "this interaction failed" while the stop actually works.
        await component.DeferAsync().ConfigureAwait(false);

        var conversationId = component.Channel.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (StopRequested is { } handler)
            await handler(conversationId).ConfigureAwait(false);
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

        if (await _client.GetChannelAsync(channelId).ConfigureAwait(false) is not IMessageChannel channel)
        {
            LogNoChannel(message.ConversationId);
            return;
        }

        try
        {
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

        if (string.IsNullOrEmpty(text))
            return "(no output)";

        return text.Length <= limit ? text : text[..(limit - ellipsis.Length)] + ellipsis;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Discord channel {ConversationId} to reply in")]
    private partial void LogNoChannel(string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send a Discord message to {ConversationId}")]
    private partial void LogSendFailed(Exception ex, string conversationId);
}
