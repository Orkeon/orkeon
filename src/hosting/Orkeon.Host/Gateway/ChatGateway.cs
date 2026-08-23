using Microsoft.Extensions.Logging;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Turns a message from a channel into a run, and the run back into replies (GATE-03).
/// <para>
/// The order is the contract: **authorize, then route, then acknowledge, then work.** Checking
/// authorization first is what keeps an unknown sender from costing a token or appearing in a
/// log as an accepted request. Acknowledging before working is what keeps a chat platform's
/// response window from expiring on a crew that takes minutes.
/// </para>
/// </summary>
internal sealed partial class ChatGateway
{
    private readonly ICrewRunner _runner;
    private readonly IConversationRouter _router;
    private readonly IChatAuthorizer _authorizer;
    private readonly CrewHostRegistry _registry;
    private readonly ILogger<ChatGateway> _logger;

    /// <summary>Builds the gateway over the runner and the gateway's own decisions.</summary>
    public ChatGateway(
        ICrewRunner runner,
        IConversationRouter router,
        IChatAuthorizer authorizer,
        CrewHostRegistry registry,
        ILogger<ChatGateway> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The word that stops the run a conversation is running.</summary>
    public const string StopCommand = "/stop";

    /// <summary>The word that reports what a conversation is doing.</summary>
    public const string StatusCommand = "/status";

    /// <summary>
    /// Handles one message end to end. Never throws for a reason the sender caused: a refusal,
    /// a busy crew and a failed run are all answers someone reads in a chat window.
    /// </summary>
    public async Task HandleAsync(InboundMessage message, IChatResponder responder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(responder);

        if (!_authorizer.IsAuthorized(message))
        {
            // Refused before routing: no crew, no token, no accepted-request log line. The
            // sender is told, because silence would look like a broken bot.
            LogRefused(message.Channel, message.SenderId);
            await responder.CompleteAsync(message, "You are not authorized to use this bot.", ct).ConfigureAwait(false);
            return;
        }

        var text = message.Text.Trim();

        // First-token match, not StartsWith: "/stopwatch the build" is a prompt, not a stop.
        if (IsCommand(text, StopCommand))
        {
            await StopAsync(message, responder, ct).ConfigureAwait(false);
            return;
        }

        if (IsCommand(text, StatusCommand))
        {
            await StatusAsync(message, responder, ct).ConfigureAwait(false);
            return;
        }

        // One conversation, one run — claimed atomically. A FindRun check followed by an
        // await let two interleaving messages both pass and start two runs in one thread,
        // giving the user answers nobody could tell apart.
        if (!_router.TryBegin(message.ConversationId))
        {
            await responder
                .CompleteAsync(message, "This conversation is already running. Use /stop first, or open a new thread.", ct)
                .ConfigureAwait(false);
            return;
        }

        var crewName = _router.ResolveCrew(message);
        var origin = $"{message.Channel}:{message.ConversationId}";
        HostedRunResult result;
        try
        {
            result = await _runner.RunAsync(
                crewName,
                text,
                origin,
                progress => Report(message, responder, progress, ct),
                // The acknowledgement rides on admission, not before it: acknowledging first
                // promised work — with a Stop button attached to nothing — that the very next
                // line could refuse as Busy. And attaching before acknowledging is what makes
                // that button work from the first second.
                onStarted: async runId =>
                {
                    _router.Attach(message.ConversationId, runId);
                    await responder.AcknowledgeAsync(message, $"Working on it with '{crewName}'…", ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        finally
        {
            _router.Detach(message.ConversationId);
        }

        await responder.CompleteAsync(message, result.Message, ct).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="text"/> is <paramref name="command"/> as its first word.</summary>
    private static bool IsCommand(string text, string command) =>
        text.StartsWith(command, StringComparison.OrdinalIgnoreCase)
        && (text.Length == command.Length || char.IsWhiteSpace(text[command.Length]));

    private async Task StopAsync(InboundMessage message, IChatResponder responder, CancellationToken ct)
    {
        var runId = _router.FindRun(message.ConversationId);
        var stopped = runId is not null && _registry.RequestStop(runId);

        await responder
            .CompleteAsync(message, stopped ? "Stopping." : "Nothing is running in this conversation.", ct)
            .ConfigureAwait(false);
    }

    private async Task StatusAsync(InboundMessage message, IChatResponder responder, CancellationToken ct)
    {
        var runId = _router.FindRun(message.ConversationId);
        var run = runId is null ? null : _registry.Running.FirstOrDefault(r => r.Id == runId);

        var text = run is null
            ? "Nothing is running in this conversation."
            : $"Running '{run.CrewName}' for {Elapsed(run.StartedAt)} (run {run.Id}).";

        await responder.CompleteAsync(message, text, ct).ConfigureAwait(false);
    }

    /// <summary>"3m 12s", not an ISO timestamp: /status answers a person, not a parser.</summary>
    private static string Elapsed(DateTimeOffset startedAt)
    {
        var elapsed = DateTimeOffset.UtcNow - startedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m"
            : elapsed.TotalMinutes >= 1
                ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s"
                : $"{elapsed.Seconds}s";
    }

    private static void Report(InboundMessage message, IChatResponder responder, string text, CancellationToken ct)
    {
        // Progress is best-effort by design: a channel that rate-limits or drops a progress
        // line must not fail the run that was going fine.
        _ = responder.ProgressAsync(message, text, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a message from {Channel} sender {SenderId}: not on the allow list")]
    private partial void LogRefused(string channel, string senderId);
}
