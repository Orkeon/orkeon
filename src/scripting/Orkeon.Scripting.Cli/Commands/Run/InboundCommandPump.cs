using System.Text.Json;
using System.Threading.Channels;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>
/// Reads the inbound stream once and routes each line by its <c>kind</c> (BUS-05).
/// <para>
/// There can only be one reader. BUS-04's channel drained stdin looking for
/// <c>input.given</c> and dropped everything else, which was correct while human answers were
/// the only thing arriving. The moment the bridge also listens — <c>post</c>, <c>send</c>,
/// <c>publish</c>, <c>subscribe</c> — a second reader would race the first, and the lines it
/// wanted would already have been swallowed. So the pump reads, and hands each line to whoever
/// asked for that kind.
/// </para>
/// <para>
/// The read loop never *executes* a command — it parses the kind and queues the line. A
/// <c>send</c> can legitimately take its whole timeout to run, and stdin blocked behind it
/// would deadlock the very <c>reply</c> the peer is trying to deliver.
/// </para>
/// <para>
/// Malformed lines are ignored, never fatal: the outbound stream is the contract, the inbound
/// one is tolerant.
/// </para>
/// </summary>
internal sealed class InboundCommandPump : IAnswerChannel, IAsyncDisposable
{
    private readonly TextReader _input;
    private readonly Func<string, CancellationToken, Task>? _onCommand;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();

    // Waiters live in a list under the gate, in arrival order. A queue looked simpler and was
    // wrong three ways: a named answer that skipped waiters re-enqueued them at the tail (so
    // "oldest" stopped meaning oldest), a cancelled waiter stayed forever (a leak), and an
    // answer handed to a dead waiter was silently lost instead of reaching the live one behind.
    private readonly List<Waiter> _waiters = [];
    private bool _closed;

    private readonly Channel<string> _commands = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private Task? _loop;
    private Task? _commandWorker;

    private sealed record Waiter(string CorrelationId, TaskCompletionSource<string?> Completion);

    /// <summary>
    /// Builds the pump over <paramref name="input"/> (stdin in the CLI).
    /// </summary>
    /// <param name="input">The inbound stream, one JSON document per line.</param>
    /// <param name="onCommand">
    /// Receives every line that is not a human answer — the hub bridge, when one is wired.
    /// A handler that throws does not stop the pump: one bad command must not deafen the run.
    /// </param>
    public InboundCommandPump(TextReader input, Func<string, CancellationToken, Task>? onCommand = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _onCommand = onCommand;
    }

    /// <inheritdoc />
    public Task<string?> ReadAnswerAsync(string correlationId, CancellationToken cancellationToken)
    {
        EnsureRunning();

        var waiter = new Waiter(
            correlationId,
            new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously));

        lock (_gate)
        {
            // A waiter registered after the channel closed would wait for a reader that will
            // never run again. Silence is an answer, and it means refusal.
            if (_closed)
                return Task.FromResult<string?>(null);

            _waiters.Add(waiter);
        }

        return WaitAsync(waiter, cancellationToken);
    }

    private async Task<string?> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        // Cancellation resolves to "no answer", which every caller reads as a refusal.
        await using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<string?>)state!).TrySetResult(null),
            waiter.Completion).ConfigureAwait(false);

        try
        {
            return await waiter.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // Whatever resolved it — an answer, cancellation, close — the entry leaves the
            // list. A cancelled waiter left behind is a leak on a long run, and worse: a later
            // unnamed answer would be spent on the corpse instead of the live waiter behind it.
            lock (_gate)
                _waiters.Remove(waiter);
        }
    }

    /// <summary>Starts the reading loop on first use — a run with no inbound traffic pays nothing.</summary>
    public void EnsureRunning()
    {
        lock (_gate)
        {
            if (_loop is not null)
                return;

            _loop = Task.Run(() => PumpAsync(_stopping.Token), CancellationToken.None);
            if (_onCommand is not null)
                _commandWorker = Task.Run(() => RunCommandsAsync(_stopping.Token), CancellationToken.None);
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
                break;      // the channel closed: no further answer will come

            Route(line);
        }

        Close();
    }

    /// <summary>
    /// Releases everyone still waiting and refuses future waiters: silence is an answer of its
    /// own, and callers read it as a refusal rather than hanging forever.
    /// </summary>
    private void Close()
    {
        Waiter[] pending;
        lock (_gate)
        {
            _closed = true;
            pending = [.. _waiters];
            _waiters.Clear();
        }

        foreach (var orphan in pending)
            orphan.Completion.TrySetResult(null);

        _commands.Writer.TryComplete();
    }

    private void Route(string line)
    {
        var kind = TryReadKind(line);
        if (kind is null)
            return;

        if (string.Equals(kind, RunEventKinds.InputGiven, StringComparison.Ordinal))
        {
            CompleteWaiter(line);
            return;
        }

        if (_onCommand is not null)
            _commands.Writer.TryWrite(line);
    }

    private async Task RunCommandsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var line in _commands.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await _onCommand!(line, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A command that fails is the sender's problem, not the run's. Swallowing
                    // here is what keeps one bad line from stopping the pump for everyone else.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private void CompleteWaiter(string line)
    {
        if (!TryReadAnswer(line, out var correlationId, out var value))
            return;

        // Everything under the gate: matching and resolution must be atomic against waiters
        // arriving, cancelling and leaving.
        lock (_gate)
        {
            if (correlationId is not null)
            {
                // Named answers are matched by id; anyone the answer skips over stays where
                // they are, in arrival order.
                var named = _waiters.Find(w => string.Equals(w.CorrelationId, correlationId, StringComparison.Ordinal));
                if (named is not null)
                {
                    _waiters.Remove(named);
                    named.Completion.TrySetResult(value);
                }

                return;
            }

            // An answer naming nobody answers the oldest *live* waiter: a human typing into a
            // terminal has no identifier to quote, and refusing them would be silly. A waiter
            // whose completion no longer accepts (cancelled between resolution and removal)
            // must not consume the answer — that would lose it for the live one behind.
            while (_waiters.Count > 0)
            {
                var oldest = _waiters[0];
                _waiters.RemoveAt(0);
                if (oldest.Completion.TrySetResult(value))
                    return;
            }
        }
    }

    /// <summary>Reads the <c>kind</c> of an inbound line, or null when the line is unusable.</summary>
    internal static string? TryReadKind(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                ? kind.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses an <c>input.given</c> line into its correlation id (optional) and value.</summary>
    internal static bool TryReadAnswer(string line, out string? correlationId, out string? value)
    {
        correlationId = null;
        value = null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("correlationId", out var id) && id.ValueKind == JsonValueKind.String)
                correlationId = id.GetString();

            value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        // The read loop is deliberately NOT awaited. Console stdin has no truly cancellable
        // read: TextReader.ReadLineAsync(ct) checks the token and then blocks in ReadLine(),
        // so a parent that keeps our stdin open (Studio does) would hold this await — and the
        // whole process — hostage after run.finished. The loop runs on a background pool
        // thread; abandoning it is safe, and Close() below releases every waiter it guarded.
        Close();

        if (_commandWorker is not null)
        {
            try
            {
                await _commandWorker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _stopping.Dispose();
    }
}
