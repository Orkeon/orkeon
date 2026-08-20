using System.Collections.Concurrent;
using System.Text.Json;

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
/// Malformed lines are ignored, never fatal: the outbound stream is the contract, the inbound
/// one is tolerant.
/// </para>
/// </summary>
internal sealed class InboundCommandPump : IAnswerChannel, IAsyncDisposable
{
    private readonly TextReader _input;
    private readonly Func<string, CancellationToken, Task>? _onCommand;
    private readonly ConcurrentQueue<Waiter> _waiters = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;
    private readonly Lock _startGate = new();

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
        _waiters.Enqueue(waiter);

        return WaitAsync(waiter, cancellationToken);
    }

    private static async Task<string?> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        // Cancellation resolves to "no answer", which every caller reads as a refusal.
        await using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<string?>)state!).TrySetResult(null),
            waiter.Completion).ConfigureAwait(false);

        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>Starts the reading loop on first use — a run with no inbound traffic pays nothing.</summary>
    public void EnsureRunning()
    {
        lock (_startGate)
            _loop ??= Task.Run(() => PumpAsync(_stopping.Token), CancellationToken.None);
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

            await RouteAsync(line, ct).ConfigureAwait(false);
        }

        // Release everyone still waiting: silence is an answer of its own, and callers read it
        // as a refusal rather than hanging forever.
        while (_waiters.TryDequeue(out var orphan))
            orphan.Completion.TrySetResult(null);
    }

    private async Task RouteAsync(string line, CancellationToken ct)
    {
        var kind = TryReadKind(line);
        if (kind is null)
            return;

        if (string.Equals(kind, RunEventKinds.InputGiven, StringComparison.Ordinal))
        {
            CompleteWaiter(line);
            return;
        }

        if (_onCommand is null)
            return;

        try
        {
            await _onCommand(line, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A command that fails is the sender's problem, not the run's. Swallowing here is
            // what keeps one bad line from stopping the pump for everyone else.
        }
    }

    private void CompleteWaiter(string line)
    {
        if (!TryReadAnswer(line, out var correlationId, out var value))
            return;

        // An answer naming nobody answers whoever is waiting: a human typing into a terminal
        // has no identifier to quote, and refusing them would be silly.
        if (correlationId is null)
        {
            if (_waiters.TryDequeue(out var oldest))
                oldest.Completion.TrySetResult(value);
            return;
        }

        // Named answers are matched by id; anyone the answer skips over stays queued.
        var skipped = new List<Waiter>();
        while (_waiters.TryDequeue(out var candidate))
        {
            if (string.Equals(candidate.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                candidate.Completion.TrySetResult(value);
                break;
            }

            skipped.Add(candidate);
        }

        foreach (var waiter in skipped)
            _waiters.Enqueue(waiter);
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

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _stopping.Dispose();
    }
}
