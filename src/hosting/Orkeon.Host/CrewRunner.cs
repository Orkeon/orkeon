using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Hosting;

namespace Orkeon.Host;

/// <summary>Why a run did not happen, or how it ended.</summary>
internal enum HostedRunOutcome
{
    /// <summary>The crew ran to completion.</summary>
    Completed,

    /// <summary>The configuration declares no crew by that name.</summary>
    UnknownCrew,

    /// <summary>The crew is already at its concurrency limit.</summary>
    Busy,

    /// <summary>The host is stopping: the run was not started (GAP-35).</summary>
    HostStopping,

    /// <summary>Someone asked it to stop, or the host is shutting down.</summary>
    Cancelled,

    /// <summary>It failed.</summary>
    Failed,

    /// <summary>The run stopped on a question to a human and waits for the answer (FWK-23).</summary>
    WaitingForInput,
}

/// <summary>What a hosted run produced.</summary>
internal sealed record HostedRunResult(HostedRunOutcome Outcome, string? RunId, string Message)
{
    /// <summary>Whether the crew actually ran and finished.</summary>
    public bool Succeeded => Outcome == HostedRunOutcome.Completed;

    /// <summary>The files of the conversation's message that were not taken.</summary>
    public IReadOnlyList<RefusedFile> RefusedFiles { get; init; } = [];

    /// <summary>The files of the conversation's outbox that are new or changed, whatever the outcome.</summary>
    public IReadOnlyList<OutboxFile> OutboxFiles { get; init; } = [];

    /// <summary>The images of the message that an agent was not shown, whatever the outcome.</summary>
    public IReadOnlyList<Application.Interfaces.Services.UnseenImage> UnseenImages { get; init; } = [];
}

/// <summary>
/// Runs a hosted crew. A port so the gateway can be exercised without a host, a model or a
/// crew — the routing, the authorization and the reply order are worth testing on their own.
/// </summary>
internal interface ICrewRunner
{
    /// <summary>
    /// Runs <paramref name="crewName"/> and reports as it goes. Deliberately takes no
    /// cancellation token: a hosted run is stopped through the registry (a Stop press, a
    /// <c>DELETE</c> of its A2A task, the drain, its own deadline), never by a caller's ambient
    /// token — linking the two is how SIGTERM used to kill every run at t=0 while the grace
    /// period waited for corpses.
    /// </summary>
    Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Func<string, Task>? onStarted = null,
        IReadOnlyDictionary<string, string>? variables = null,
        RunIdempotency? idempotency = null,
        HostedConversation? conversation = null);
}

/// <summary>The idempotency key of a run asked over the host API, and the hash of the request it came with (FWK-14).</summary>
/// <param name="Key">The client's key.</param>
/// <param name="Hash">The hash of the request, to tell a replay from a different request under the same key.</param>
internal sealed record RunIdempotency(string Key, string Hash);

/// <summary>
/// Runs one hosted crew, in its own dependency-injection scope (GATE-02).
/// <para>
/// **The scope is the isolation.** <c>IMemoryScope</c> is registered scoped, so two runs
/// sharing a provider would share a memory — the leak the gateway specification calls the most
/// serious risk of this design. Creating a scope per run is what keeps one conversation from
/// reading another's, and a test holds it rather than the comment alone.
/// </para>
/// <para>
/// A run also carries its own deadline. A daemon has nobody watching to press Ctrl-C, so a run
/// with no timeout is a stuck daemon waiting for a model that will never answer.
/// </para>
/// </summary>
internal sealed partial class CrewRunner : ICrewRunner
{
    /// <summary>
    /// What a person in a chat thread, or an A2A peer, reads when it asks for a run while the host
    /// stops: why nothing started, and that the same request will work once the host is back.
    /// </summary>
    internal const string HostStoppingMessage =
        "The host is stopping: this run was not started. Send it again once the host is back.";

    private readonly IServiceScopeFactory _scopes;
    private readonly CrewHostRegistry _registry;
    private readonly IHost _host;
    private readonly OrkeonHostOptions _options;
    private readonly ILogger<CrewRunner> _logger;

    /// <summary>
    /// Maps a hosted crew's name to the virtual path its directory was mounted under.
    /// Optional so a test can compose a runner without the boot-time plan; absent, the
    /// configured path is used verbatim, which is what a caller mounting its own crews wants.
    /// </summary>
    private readonly HostCrewMountPlan? _mountPlan;

    private readonly TimeProvider _time;
    private readonly Orkeon.Application.Interfaces.Checkpointing.IPendingInputBroker? _broker;
    private readonly Files.ConversationFiles? _files;
    private readonly Files.DownloadableFiles? _downloads;

    /// <summary>Builds the runner over the host's services and its registry.</summary>
    public CrewRunner(
        IServiceScopeFactory scopes,
        CrewHostRegistry registry,
        IHost host,
        IOptions<OrkeonHostOptions> options,
        ILogger<CrewRunner> logger,
        HostCrewMountPlan? mountPlan = null,
        Orkeon.Application.Interfaces.Checkpointing.IPendingInputBroker? broker = null,
        Files.ConversationFiles? files = null,
        Files.CrewFileFolders? fileFolders = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _time = host.Services.GetService<TimeProvider>() ?? TimeProvider.System;
        _broker = broker;
        _files = files;
        _downloads = files is null || fileFolders is null ? null : new Files.DownloadableFiles(files, fileFolders);
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _host = host;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mountPlan = mountPlan;
    }

    /// <summary>
    /// Runs <paramref name="crewName"/> against <paramref name="prompt"/>, reporting through
    /// <paramref name="onProgress"/> as tasks finish. Refusals come back as results, never as
    /// exceptions: "unknown crew", "busy" and "the host is stopping" are answers a channel relays
    /// to a person.
    /// </summary>
    /// <param name="crewName">Which hosted crew to run.</param>
    /// <param name="prompt">What to run it on.</param>
    /// <param name="origin">Where the request came from, for the registry and the logs.</param>
    /// <param name="onProgress">Receives progress lines as the run advances.</param>
    /// <param name="onStarted">
    /// Receives the run identifier the moment the slot is reserved — before the crew loads, let
    /// alone finishes. A caller that only learned the id at the end could never stop the run it
    /// started, which is what <c>/stop</c> is for.
    /// </param>
    /// <param name="variables">
    /// The run's input variables, besides the prompt — what an A2A task's <c>metadata</c> carries
    /// (GAP-23); a chat message has none.
    /// </param>
    /// <param name="idempotency">
    /// The idempotency key and request hash of a run asked over the host API (FWK-14), written into
    /// <c>run.started</c> so a restarted host still finds the key.
    /// </param>
    /// <param name="conversation">
    /// The chat conversation the run belongs to, null for a run of the host API or of an A2A peer. It is prepared after
    /// <paramref name="onStarted"/>, under the run's token and deadline.
    /// </param>
    public async Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Func<string, Task>? onStarted = null,
        IReadOnlyDictionary<string, string>? variables = null,
        RunIdempotency? idempotency = null,
        HostedConversation? conversation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewName);

        var hosted = _registry.Find(crewName);
        if (hosted is null)
            return new HostedRunResult(HostedRunOutcome.UnknownCrew, null, $"No hosted crew named '{crewName}'.");

        var run = _registry.TryStart(crewName, origin, out var refusal);
        if (run is null && refusal == AdmissionRefusal.HostStopping)
        {
            // The stop has begun (GAP-35): nothing starts, and whoever asked reads why, and that
            // the same request works once the host is back — not "busy", which tells them to try
            // again in a moment against a host that is going away.
            LogRunRefusedWhileStopping(hosted.Name, origin);
            return new HostedRunResult(HostedRunOutcome.HostStopping, null, HostStoppingMessage);
        }

        if (run is null)
        {
            return new HostedRunResult(
                HostedRunOutcome.Busy,
                null,
                $"'{hosted.Name}' is already running {hosted.Profile.MaxConcurrentRuns} run(s); try again shortly.");
        }

        LogRunStarted(run.Id, hosted.Name, origin);

        Files.ConversationRun? folders = null;
        IReadOnlyList<RefusedFile> refusedFiles = [];
        HostedRunResult? result = null;
        try
        {
            // The deadline is armed BEFORE the acknowledgement: the ack is a network round
            // trip, and a rate-limited channel could otherwise hold an admitted slot with no
            // timeout at all.
            // The clock stops while the run holds on a question to a person (FWK final review H1): the hold timeout,
            // not this one, ends that wait.
            using var deadline = new RunDeadline(_time, _options.RunTimeout, _broker, run.Id, run.Cancellation.Cancel);

            // Inside the try: a throwing callback must release the slot like any other
            // failure, or the crew stays one seat closer to permanently "busy".
            if (onStarted is not null)
                await onStarted(run.Id).ConfigureAwait(false);

            if (_files is not null)
            {
                // Before the preparation, which keeps files in them, and to the end of the run: retention leaves them.
                var (opened, refused) = await OpenFoldersAsync(_files, conversation, run, hosted.Name).ConfigureAwait(false);
                if (opened is null)
                    return refused!;
                folders = opened;
            }

            if (conversation is not null)
            {
                var (prepared, ended) = await PrepareAsync(conversation, run, hosted.Name).ConfigureAwait(false);
                if (prepared is null)
                    return ended!;
                (prompt, variables) = prepared;
                refusedFiles = prepared.RefusedFiles;
                if (folders is not null)
                    folders.Images = prepared.Images;
            }

            // Read at each call of chat_attach, by everything the run starts and by no other run; closed with the run, even
            // when what it started outlives it. Entered without one too: no conversation is inherited from a caller.
            using var attachments = AttachmentsOf(conversation, hosted, run);
            using var entered = Orkeon.Hosting.Conversations.ConversationScope.Enter(attachments);
            result = await RunMountedAsync(hosted, run, folders, prompt, onProgress, variables, idempotency).ConfigureAwait(false)
                with { RefusedFiles = refusedFiles };
            return result;
        }
        catch (OperationCanceledException)
        {
            return Cancelled(run, hosted.Name) with { RefusedFiles = refusedFiles };
        }
        catch (ConversationPreparationException ex)
        {
            LogPreparationFailed(ex, run.Id, hosted.Name);
            return new HostedRunResult(HostedRunOutcome.Failed, run.Id, ex.Message) { RefusedFiles = refusedFiles };
        }
#pragma warning disable CA1031 // A hosted run must not take the daemon down with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRunFailed(ex, run.Id, hosted.Name);
            return new HostedRunResult(HostedRunOutcome.Failed, run.Id, FailureMessage(run.Id)) { RefusedFiles = refusedFiles };
        }
        finally
        {
            // Inside the slot: a drain waits for it, and the crew's next run finds the folders settled.
            if (folders is not null)
                await folders.CloseAsync(result?.Outcome == HostedRunOutcome.WaitingForInput).ConfigureAwait(false);
            _registry.Finish(run.Id);
        }
    }

    /// <summary>The run's folders, or the result their refusal is: one the person reads, never the detail.</summary>
    private async Task<(Files.ConversationRun? Opened, HostedRunResult? Ended)> OpenFoldersAsync(
        Files.ConversationFiles files, HostedConversation? conversation, HostedRun run, string crewName)
    {
        try
        {
            return (await files.OpenRunAsync(conversation, run.Id, run.Cancellation.Token).ConfigureAwait(false), null);
        }
        catch (ConversationPreparationException ex)
        {
            LogPreparationFailed(ex, run.Id, crewName);
            return (null, new HostedRunResult(HostedRunOutcome.Failed, run.Id, ex.Message));
        }
    }

    private Files.ConversationAttachments? AttachmentsOf(HostedConversation? conversation, HostedCrewOptions hosted, HostedRun run) =>
        _downloads is not null && conversation is { SendFilesAsync: { } send }
            ? new Files.ConversationAttachments(
                _downloads, _options.Files, new ConversationRef(conversation.Channel, conversation.ConversationId), hosted, run, send, _logger)
            : null;

    /// <summary>
    /// The run in its own mount namespace, entered here and left when this returns, and what it changed in
    /// <c>/outbox</c>, read before the namespace is left, whatever the outcome.
    /// </summary>
    private async Task<HostedRunResult> RunMountedAsync(
        HostedCrewOptions hosted,
        HostedRun run,
        Files.ConversationRun? folders,
        string prompt,
        Action<string>? onProgress,
        IReadOnlyDictionary<string, string>? variables,
        RunIdempotency? idempotency)
    {
        // The run's own mount namespace, when the host grants this crew folders of its
        // own, or keeps its conversations' files. Ambient rather than DI-scoped on purpose: IFileSystemService is a singleton
        // that many singletons inject, so it consults the ambient override per operation
        // instead of being resolved per scope. Two hosted crews may therefore both address
        // /output over two different physical folders — which the one flat boot registry,
        // where a virtual path must be globally unique, cannot express.
        using var mounts = EnterMountNamespace(hosted, folders);

        var before = folders is null ? null : await folders.ListOutboxAsync().ConfigureAwait(false);
        var result = await RunCrewAsync(hosted, run, folders, prompt, onProgress, variables, idempotency).ConfigureAwait(false);
        if (folders is null)
            return result;

        var after = await folders.ListOutboxAsync().ConfigureAwait(false);
        return result with
        {
            OutboxFiles =
            [
                .. after
                    .Where(f => !before!.TryGetValue(f.Key, out var was) || was != f.Value)
                    .OrderBy(f => f.Key, StringComparer.Ordinal)
                    .Select(f => new OutboxFile(f.Key, f.Value.Size)),
            ],
        };
    }

    /// <summary>Loads the crew and runs it. Whatever it throws is a result.</summary>
    private async Task<HostedRunResult> RunCrewAsync(
        HostedCrewOptions hosted,
        HostedRun run,
        Files.ConversationRun? folders,
        string prompt,
        Action<string>? onProgress,
        IReadOnlyDictionary<string, string>? variables,
        RunIdempotency? idempotency)
    {
        Orkeon.Domain.Common.CrewId? crewId = null;
        try
        {
            // One scope per run. Anything registered scoped belongs to this run and dies with
            // it — the crew repository above all, which is what keeps one conversation's crew
            // out of another's resolution. Per-crew memory is released in the finally: a
            // hosted run's memory cannot outlive it by construction (every message loads a
            // fresh crew with a fresh id).
            using var scope = _scopes.CreateScope();

            // The run history (FWK-08): the run's identity and run.started before anything resolves the run's
            // hooks; run.finished when this block ends. Nothing happens when the history is off.
            var recording = scope.ServiceProvider.GetService<HostRunRecording>();
            using var history = recording?.Begin(hosted.Name, run.Id, () => run.Cancellation.IsCancellationRequested, idempotency);
            var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();

            // Task completions flow to the conversation as they happen — the throttled
            // responder downstream spaces them out. Resolved from the run's own scope, so
            // two concurrent runs never share a counter or a callback.
            if (onProgress is not null && scope.ServiceProvider.GetService<RunProgressHook>() is { } progressHook)
                progressHook.OnProgress = onProgress;

            var (mapped, isDirectory) = ResolveMountedPath(hosted.Name);
            var crew = await RunnerExecution
                .LoadCrewAsync(_host, factory, scope.ServiceProvider, mapped ?? hosted.Path, _logger, run.Cancellation.Token, isDirectory)
                .ConfigureAwait(false);
            crewId = crew.Id;
            if (folders is { OfConversation: true })
                await WarnOfScopesWithoutInboxAsync(scope.ServiceProvider, crew, hosted.Name, run.Cancellation.Token).ConfigureAwait(false);

            onProgress?.Invoke($"Running '{hosted.Name}'…");

            var input = variables is { Count: > 0 }
                ? Application.Interfaces.Services.CrewInput.WithStringVariables(prompt, variables)
                : Application.Interfaces.Services.CrewInput.Empty(prompt);
            if (folders is not null)
                input = input with { Images = folders.Images };
            // The kickoff adopts the identity the recording opened (the run's ExecutionId), and carries the crew key.
            var output = await orchestrator
                .KickoffAsync(
                    crew.Id,
                    input,
                    RunnerRunIdentity.For(scope.ServiceProvider, crew.Name, mapped ?? hosted.Path, hosted.Path, resume: null),
                    run.Cancellation.Token)
                .ConfigureAwait(false);
            if (recording is not null)
            {
                recording.Succeeded = output.Succeeded;
                recording.WaitingForInput = output.PendingInput is not null;
                recording.DecisionUnavailable = output.ErrorCode == Orkeon.Constants.Protocol.RunEventErrorCodes.DecisionUnavailable;
            }

            return ResultOf(output, run, hosted.Name) with { UnseenImages = output.UnseenImages };
        }
        catch (OperationCanceledException)
        {
            return Cancelled(run, hosted.Name);
        }
#pragma warning disable CA1031 // A hosted run must not take the daemon down with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The detail goes to the log, not to the chat: a load failure carries absolute
            // server paths and a provider failure carries endpoints, and the thread's
            // membership is not the operator set.
            LogRunFailed(ex, run.Id, hosted.Name);
            return new HostedRunResult(HostedRunOutcome.Failed, run.Id, FailureMessage(run.Id));
        }
        finally
        {
            ReleasePerRunState(crewId);
        }
    }

    /// <summary>A declared file-system scope is exhaustive: an agent whose own leaves <c>/inbox</c> out reads no file people send.</summary>
    private async Task WarnOfScopesWithoutInboxAsync(IServiceProvider services, Domain.Crew.Crew crew, string crewName, CancellationToken ct)
    {
        var agents = await services.GetRequiredService<Domain.Agent.IAgentRepository>().GetByIdsAsync(crew.Agents, ct).ConfigureAwait(false);
        foreach (var agent in agents.Where(a => a.FileSystemScope.Count > 0
            && !a.FileSystemScope.Any(e => e.Path.TrimEnd('/') is "" or RunnerVirtualRoots.Inbox)))
        {
            LogScopeWithoutInbox(agent.Role.Value, crewName);
        }
    }

    /// <summary>The conversation's preparation, or the result its failure is: one the person reads, never the detail.</summary>
    private async Task<(PreparedRun? Prepared, HostedRunResult? Ended)> PrepareAsync(
        HostedConversation conversation, HostedRun run, string crewName)
    {
        try
        {
            return (await conversation.PrepareAsync(run.Cancellation.Token).ConfigureAwait(false), null);
        }
#pragma warning disable CA1031 // Any failure of the preparation, a cancellation of its own included, is this run's answer.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Stopped or out of time: whatever the preparation threw then is the stop's doing.
            if (run.Cancellation.IsCancellationRequested)
                return (null, Cancelled(run, crewName));

            LogPreparationFailed(ex, run.Id, crewName);
            return (null, new HostedRunResult(
                HostedRunOutcome.Failed,
                run.Id,
                ex is ConversationPreparationException
                    ? ex.Message
                    : $"This message could not be prepared (run {run.Id}). Details are in the host log."));
        }
    }

    private HostedRunResult ResultOf(CrewOutput output, HostedRun run, string crewName)
    {
        // KickoffAsync never throws — its fault barrier converts everything into an
        // output. Reading that output as Completed reported every timeout, /stop and
        // crew failure as a success, logged "finished", and handed the user the
        // apology string as if it were the answer. Order matters here: a completed
        // answer beats a stop that raced in after the fact — the user already paid
        // for it, and "The run was stopped." would throw it away.
        if (output.Succeeded)
        {
            LogRunFinished(run.Id, crewName);
            return new HostedRunResult(
                HostedRunOutcome.Completed,
                run.Id,
                output.FinalOutput ?? string.Empty);
        }

        if (output.PendingInput is { } waiting)
        {
            return new HostedRunResult(
                HostedRunOutcome.WaitingForInput,
                run.Id,
                $"The run is waiting for input ({waiting.RequestId}): {Orkeon.Infrastructure.Runs.RedactingRunEventSink.RedactText(waiting.Prompt)}");
        }

        if (run.Cancellation.IsCancellationRequested)
            return Cancelled(run, crewName);

        LogRunFailedWithOutput(run.Id, crewName, output.FinalOutput ?? string.Empty);
        return new HostedRunResult(HostedRunOutcome.Failed, run.Id, FailureMessage(run.Id));
    }

    // A crew DIRECTORY is mounted as the root itself; a crew FILE is addressed inside
    // one. So the plan already answers "directory?", and answering it here rather than
    // asking the VFS again is what keeps a symlinked crew directory loading.
    private (string? Mapped, bool? IsDirectory) ResolveMountedPath(string crewName)
    {
        var mapped = _mountPlan?.VirtualPaths.TryGetValue(crewName, out var virtualPath) == true
            ? virtualPath
            : null;
        var isDirectory = mapped is not null
            ? _mountPlan!.Roots.Contains(mapped, StringComparer.Ordinal)
            : (bool?)null;
        return (mapped, isDirectory);
    }

    private static string FailureMessage(string runId) =>
        $"The run failed (run {runId}). Details are in the host log.";

    /// <summary>
    /// One cancelled token, two different stories: a person pressed Stop, or the run hit its
    /// own deadline. The registry recorded which, and the user-facing sentence and the log
    /// line both say the right one.
    /// </summary>
    private HostedRunResult Cancelled(HostedRun run, string crewName)
    {
        if (run.StopRequested)
        {
            LogRunCancelled(run.Id, crewName);
            return new HostedRunResult(HostedRunOutcome.Cancelled, run.Id, "The run was stopped.");
        }

        LogRunTimedOut(run.Id, crewName, _options.RunTimeout);
        return new HostedRunResult(
            HostedRunOutcome.Cancelled, run.Id,
            $"The run timed out after {_options.RunTimeout}.");
    }

    /// <summary>
    /// Forgets what the process-wide services accumulated for this run's crew. Every hosted
    /// message loads a fresh crew with a fresh id, and the memory service and the provider
    /// registry are singletons keyed by that id: without this, a daemon leaks one entry per
    /// conversation, forever.
    /// </summary>
    private void ReleasePerRunState(Orkeon.Domain.Common.CrewId? crewId)
    {
        if (crewId is null)
            return;

        _host.Services.GetService<Application.Interfaces.Services.IMemoryService>()?.ReleaseMemorySystem(crewId);
        _host.Services.GetService<Application.Memory.CrewMemoryProviderRegistry>()?.Remove(crewId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent '{Agent}' of '{CrewName}' declares a filesystem: without /inbox: it does not read the files people send")]
    private partial void LogScopeWithoutInbox(string agent, string crewName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started: crew '{CrewName}' from {Origin}")]
    private partial void LogRunStarted(string runId, string crewName, string origin);

    [LoggerMessage(Level = LogLevel.Information, Message = "A run of '{CrewName}' from {Origin} was refused: the host is stopping")]
    private partial void LogRunRefusedWhileStopping(string crewName, string origin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} of '{CrewName}' finished")]
    private partial void LogRunFinished(string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} of '{CrewName}' was stopped before finishing")]
    private partial void LogRunCancelled(string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} of '{CrewName}' timed out after {Timeout}")]
    private partial void LogRunTimedOut(string runId, string crewName, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "The conversation of run {RunId} of '{CrewName}' could not be prepared")]
    private partial void LogPreparationFailed(Exception ex, string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} of '{CrewName}' failed")]
    private partial void LogRunFailed(Exception ex, string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} of '{CrewName}' failed: {Output}")]
    private partial void LogRunFailedWithOutput(string runId, string crewName, string output);

    /// <summary>
    /// Enters the mount namespace of a run: the folders this crew was granted and the conversation's, or nothing when it
    /// has neither.
    /// </summary>
    /// <remarks>
    /// The composed registry carries the boot Internal mounts forward — entering a scope
    /// REPLACES the mount set rather than merging with it, so a registry built from the crew's
    /// own folders alone would take <c>/llm-logs</c> and <c>/sandbox</c> down with it for the
    /// length of the run, and with them the check that stops either gaining a second,
    /// agent-reachable address through one of the crew's own mounts.
    /// </remarks>
    private MountNamespaceLease? EnterMountNamespace(HostedCrewOptions hosted, Files.ConversationRun? folders)
    {
        if (folders is null && hosted.Mounts.Count == 0)
            return null;

        var ambient = _host.Services.GetService<IFileSystemScope>();
        var boot = _host.Services.GetService<FileSystemRegistry>();
        if (ambient is null || boot is null)
            return null;

        var granted = GrantedMounts(hosted);
        if (folders is null && granted.Count == 0)
            return null;

        var registry = folders?.Registry(hosted) ?? ScopedMountComposition.ForExecution(boot, granted);
        return new MountNamespaceLease(registry, ambient.Enter(registry));
    }

    /// <summary>The folders the host grants a crew.</summary>
    internal static List<FileSystemMount> GrantedMounts(HostedCrewOptions crew) =>
        [.. crew.Mounts.Where(m => !string.IsNullOrWhiteSpace(m)).Select(FileSystemMount.Parse)];

    /// <summary>Releases the ambient override first, then the registry it pointed at.</summary>
    private sealed class MountNamespaceLease(FileSystemRegistry registry, IDisposable token) : IDisposable
    {
        public void Dispose()
        {
            token.Dispose();
            registry.Dispose();
        }
    }

}
