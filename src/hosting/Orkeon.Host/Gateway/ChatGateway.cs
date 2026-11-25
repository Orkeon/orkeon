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
    private readonly Files.ConversationFiles? _files;

    /// <summary>Builds the gateway over the runner and the gateway's own decisions.</summary>
    public ChatGateway(
        ICrewRunner runner,
        IConversationRouter router,
        IChatAuthorizer authorizer,
        CrewHostRegistry registry,
        ILogger<ChatGateway> logger,
        Files.ConversationFiles? files = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _files = files;
    }

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

        // /stop and /status are REGISTERED slash commands (CommandInvoked on the channel),
        // not text this gateway parses: the platform's client intercepts the slash, and one
        // path means one authorization check. A literal "/stop" typed as plain text is a
        // prompt like any other.
        var text = message.Text.Trim();

        // Nothing to run on. Said before the conversation is claimed: an empty prompt is no run, and silence looks like
        // a broken bot. Where no file is taken, files alone are nothing to run on either.
        var source = _files is null ? null : message.AttachmentSource;
        var notTaken = source is null && message.Attachments.Count > 0 ? FileNotices.NotTaken(message.Attachments) : null;
        if (text.Length == 0 && (source is null || message.Attachments.Count == 0))
        {
            const string nothing = "There is nothing to work on: the message has no text.";
            await responder.CompleteAsync(message, notTaken is null ? nothing : $"{notTaken}\n{nothing}", ct).ConfigureAwait(false);
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
        string answer;
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
                    var working = $"Working on it with '{crewName}'…";
                    await responder
                        .AcknowledgeAsync(message, notTaken is null ? working : $"{working}\n{notTaken}", ct)
                        .ConfigureAwait(false);
                },
                conversation: new HostedConversation
                {
                    Channel = message.Channel,
                    ConversationId = message.ConversationId,
                    // After admission and the acknowledgement, under the run's own token and deadline.
                    PrepareAsync = token => _files is null || source is null
                        ? Task.FromResult(new PreparedRun(text))
                        : ChatReception.PrepareAsync(_files, message, source, text, token),
                }).ConfigureAwait(false);

            // Before the conversation is free: the next message's run must not find the answer half kept.
            answer = _files is null || result.RunId is not { } ended
                ? result.Message
                : await AnswerAsync(_files, message, responder, result, ended).ConfigureAwait(false);
        }
        finally
        {
            _router.Detach(message.ConversationId);
        }

        if (result.RefusedFiles.Count > 0)
        {
            // In one message when it holds both: a long answer is cut, and must not be cut shorter by this.
            var refused = FileNotices.Refused(result.RefusedFiles);
            var both = $"{refused}\n\n{answer}";
            if (both.Length <= responder.MaxTextLength)
                answer = both;
            else
                await responder.CompleteAsync(message, refused, ct).ConfigureAwait(false);
        }

        await responder.CompleteAsync(message, answer, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The answer, then the files there are to take: named, never sent. Their place is kept first, so a long answer is cut
    /// and not them; cut, it is kept whole in <c>/outbox</c>.
    /// </summary>
    private async Task<string> AnswerAsync(
        Files.ConversationFiles files, InboundMessage message, IChatResponder responder, HostedRunResult result, string runId)
    {
        var body = string.IsNullOrWhiteSpace(result.Message) ? "(no output)" : result.Message;
        var tail = string.Empty;
        if (result.UnseenImages.Count > 0)
        {
            var unseen = FileNotices.Bounded([.. result.UnseenImages.Select(FileNotices.NotShown)], ", ", responder.MaxTextLength / 4);
            tail = $"\n\nImages not shown: {unseen}.";
        }

        if (result.OutboxFiles.Count > 0)
        {
            var take = responder.TakesFileCommands ? "/get sends one." : "This channel does not send them.";
            var names = FileNotices.Bounded(
                [.. result.OutboxFiles.Select(f => FileNotices.PathAndSize(f.Path, f.Size))], ", ", responder.MaxTextLength / 3);
            tail += $"\n\nFiles: {names}. {take}";
        }

        if (body.Length + tail.Length <= responder.MaxTextLength)
            return body + tail;

        tail = $"\n\n{await KeepAsync(files, message, body, runId).ConfigureAwait(false)}{tail}";
        return DiscordResponder.Truncate(body, responder.MaxTextLength - tail.Length) + tail;
    }

    private async Task<string> KeepAsync(Files.ConversationFiles files, InboundMessage message, string body, string runId)
    {
        try
        {
            var kept = await files
                .StoreReplyAsync(new ConversationRef(message.Channel, message.ConversationId), runId, body, CancellationToken.None)
                .ConfigureAwait(false);
            return kept.Refused is { } refused
                ? $"The whole answer could not be kept: {refused.Reason}."
                : $"The whole answer is kept as {FileNotices.Path(kept.Path!)}.";
        }
#pragma warning disable CA1031 // The run has its answer: what cannot be kept of it must not replace it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReplyNotKept(ex, message.Channel);
            return "The whole answer could not be kept.";
        }
    }

    /// <summary>
    /// Stops what this conversation is running and says so. Pure decision + wording — the
    /// caller owns the delivery, because a slash command answers ephemerally while a chat
    /// message would answer in the thread.
    /// </summary>
    public string Stop(string conversationId)
    {
        var runId = _router.FindRun(conversationId);
        var stopped = runId is not null && _registry.RequestStop(runId);

        return stopped ? "Stopping." : "Nothing is running in this conversation.";
    }

    /// <summary>What this conversation is running, and since when. Same split as Stop.</summary>
    public string Status(string conversationId)
    {
        var runId = _router.FindRun(conversationId);
        var run = runId is null ? null : _registry.Running.FirstOrDefault(r => r.Id == runId);

        return run is null
            ? "Nothing is running in this conversation."
            : $"Running '{run.CrewName}' for {Elapsed(run.StartedAt)} (run {run.Id}).";
    }

    /// <summary>"3m 12s", not an ISO timestamp: /status answers a person, not a parser.</summary>
    private static string Elapsed(DateTimeOffset startedAt)
    {
        var elapsed = DateTimeOffset.UtcNow - startedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m";

        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s"
            : $"{elapsed.Seconds}s";
    }

    private static void Report(InboundMessage message, IChatResponder responder, string text, CancellationToken ct)
    {
        // Progress is best-effort by design: a channel that rate-limits or drops a progress
        // line must not fail the run that was going fine.
        _ = responder.ProgressAsync(message, text, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The whole answer of a run from {Channel} could not be kept")]
    private partial void LogReplyNotKept(Exception ex, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a message from {Channel} sender {SenderId}: not on the allow list")]
    private partial void LogRefused(string channel, string senderId);
}
