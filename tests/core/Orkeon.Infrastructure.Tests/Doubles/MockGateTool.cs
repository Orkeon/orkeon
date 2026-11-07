using Orkeon.Domain.Tools;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A tool whose call waits at a gate: it opens when <see cref="Release"/> is called, or by itself
/// once <c>releaseWhenEntered</c> calls are inside — a barrier, so a test can require that several
/// calls overlap without a delay. It honours the cancellation token.
/// </summary>
public sealed class MockGateTool : IBaseTool
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _releaseWhenEntered;
    private int _entered;
    private int _inFlight;
    private int _maxInFlight;
    private int _cancelled;

    /// <summary>A gate tool that opens by itself when <paramref name="releaseWhenEntered"/> calls are inside.</summary>
    public MockGateTool(string name, int releaseWhenEntered = int.MaxValue)
    {
        Name = name;
        _releaseWhenEntered = releaseWhenEntered;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => Name;

    /// <inheritdoc />
    public ToolSchema Schema => new(Name, Name, new Dictionary<string, ParameterSchema>());

    /// <summary>How many calls got in.</summary>
    public int Entered => Volatile.Read(ref _entered);

    /// <summary>The most calls inside at the same moment.</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    /// <summary>How many calls saw their token cancelled.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <summary>Opens the gate.</summary>
    public void Release() => _gate.TrySetResult();

    /// <inheritdoc />
    public async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxInFlight)))
            Interlocked.CompareExchange(ref _maxInFlight, now, seen);

        if (Interlocked.Increment(ref _entered) >= _releaseWhenEntered)
            Release();
        try
        {
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ToolCallResponse(true, "done", null);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}
