using System.Text.Json;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Run;

/// <summary>The event kinds a watched run emits. Mirrors <c>RunEventKinds</c> on the CLI side.</summary>
public static class RunEventKinds
{
    /// <summary>The run began; carries the target and whether deltas were asked for.</summary>
    public const string RunStarted = "run.started";

    /// <summary>One task finished; carries its identifier, agent, outcome and duration.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = "cost.updated";

    /// <summary>A fragment of generated text, only under <c>--stream</c>.</summary>
    public const string LlmDelta = "llm.delta";

    /// <summary>The run ended; carries the outcome and the exit code.</summary>
    public const string RunFinished = "run.finished";

    /// <summary>Something went wrong, recoverably or not.</summary>
    public const string Error = "error";

    /// <summary>A task is asking a human; carries the question and, for a choice, its options.</summary>
    public const string InputNeeded = "input.needed";

    /// <summary>A human answered.</summary>
    public const string InputGiven = "input.given";

    /// <summary>Something the run's hub relayed to this process.</summary>
    public const string HubMessage = "hub.message";
}

/// <summary>
/// The Studio side of a watched run (BUS-06): launches <c>orkeon run --events jsonl</c>,
/// parses its stdout into <see cref="OrkeonEvent"/>s, and answers over stdin.
/// <para>
/// The sibling of <c>ForgeClient</c>, and deliberately its twin: same launcher, same locator,
/// same envelope parser. Studio still never touches an LLM or Infrastructure — it only ever
/// sees these lines.
/// </para>
/// <para>
/// Beyond answering questions, this client has a seat at the run's hub (BUS-05): it can post
/// to an agent, publish on a topic and subscribe to one. Whether the agents may answer is the
/// crew's decision, declared in its <c>links:</c> block.
/// </para>
/// </summary>
public sealed class RunClient
{
    private readonly IProcessLauncher _launcher;
    private readonly OrkeonBinaryLocator _locator;
    private volatile IProcessInputWriter? _input;
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates a client over an explicit launcher and locator (the test seam).</summary>
    public RunClient(IProcessLauncher launcher, OrkeonBinaryLocator locator)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    /// <summary>Creates a client over the real machine and real processes.</summary>
    public static RunClient ForCurrentMachine() =>
        new(SystemProcessLauncher.Instance, OrkeonBinaryLocator.ForCurrentMachine());

    /// <summary>Whether a run is currently alive.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Runs one crew to completion. Protocol lines reach <paramref name="onEvent"/>;
    /// everything else — stderr, unparsable stdout — reaches <paramref name="onRaw"/> and is
    /// never dropped: what the stream said stays visible, even when the screen shows progress
    /// instead of scrollback. Callbacks arrive serialized, on a background thread.
    /// </summary>
    public async Task<ProcessRunResult> RunAsync(
        RunTarget target,
        RunLaunchOptions options,
        Action<OrkeonEvent> onEvent,
        Action<ProcessOutputLine>? onRaw = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onEvent);
        if (IsRunning)
            throw new InvalidOperationException("A run is already in progress.");

        var location = _locator.Locate();
        if (!location.Found)
            return ProcessRunResult.NotStarted(location.Error ?? $"`{OrkeonBinaryLocator.ExecutableBaseName}` was not found.");

        // The screen cannot show progress it never asked for. Forcing the flag here is what
        // keeps a caller from silently getting a terminal instead.
        var watched = options with { Events = true };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = linked;
        IsRunning = true;
        try
        {
            var launch = new ProcessLaunchRequest
            {
                FileName = location.Path!,
                Arguments = RunArgumentsBuilder.Build(target, watched),
                WorkingDirectory = workingDirectory,
                OnInputReady = writer => _input = writer,
            };

            return await _launcher.RunAsync(
                launch,
                line =>
                {
                    if (line.Channel == ProcessOutputChannel.StandardOutput
                        && OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
                    {
                        onEvent(orkeonEvent!);
                    }
                    else
                    {
                        onRaw?.Invoke(line);
                    }
                },
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
            _input = null;
            _cancellation = null;
        }
    }

    /// <summary>
    /// Answers the question carried by <paramref name="correlationId"/>. False when no run is
    /// listening — the caller keeps the answer rather than believing it was delivered.
    /// </summary>
    public bool Answer(string correlationId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(value);
        return WriteLine(new { kind = RunEventKinds.InputGiven, correlationId, value });
    }

    /// <summary>Posts a payload to one mailbox (<c>agent://{crew}/{agent}</c> or <c>crew://{crew}</c>).</summary>
    public bool PostTo(string address, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return WriteLine(new { kind = "post", to = address, payload });
    }

    /// <summary>Publishes a payload on a topic every subscriber of the run can hear.</summary>
    public bool Publish(string topic, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        return WriteLine(new { kind = "publish", topic, payload });
    }

    /// <summary>Asks to hear a topic; matching messages come back as <c>hub.message</c>.</summary>
    public bool Subscribe(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        return WriteLine(new { kind = "subscribe", topic });
    }

    /// <summary>Stops hearing a topic.</summary>
    public bool Unsubscribe(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        return WriteLine(new { kind = "unsubscribe", topic });
    }

    /// <summary>Answers a question an agent asked this process, by its correlation id.</summary>
    public bool ReplyToAgent(string correlationId, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return WriteLine(new { kind = "reply", correlationId, payload });
    }

    /// <summary>Asks the running crew to stop; false when nothing runs or it was already asked.</summary>
    public bool RequestCancellation()
    {
        var cancellation = _cancellation;
        if (cancellation is null || cancellation.IsCancellationRequested)
            return false;

        cancellation.Cancel();
        return true;
    }

    private bool WriteLine(object payload) =>
        _input is { } writer && writer.TryWriteLine(JsonSerializer.Serialize(payload));
}
