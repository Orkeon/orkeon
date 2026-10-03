using System.Collections.Concurrent;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// A model that answers <c>answer</c> — at once, or, when built held, once <see cref="Release"/>
/// is called — and honours the run's token while it waits, as a real provider does: a run
/// stopped mid-call sees its call cancelled. <see cref="Started"/> completes at the first call,
/// so a test waits for a run to be under way instead of sleeping.
/// </summary>
internal sealed class HeldLlmProvider : ILlmProvider
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _answer;
    private readonly string? _failure;
    private int _cancelledCalls;

    /// <param name="answer">What every call answers.</param>
    /// <param name="held">Whether calls wait for <see cref="Release"/>.</param>
    /// <param name="failure">When set, every call throws this message instead of answering.</param>
    public HeldLlmProvider(string answer, bool held = false, string? failure = null)
    {
        _answer = answer;
        _failure = failure;
        if (!held)
            _gate.SetResult();
    }

    public string Name => "held";

    /// <summary>Completes when the first call arrives.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every prompt received, chat messages joined.</summary>
    public ConcurrentQueue<string> Prompts { get; } = new();

    /// <summary>How many calls were cancelled while they waited.</summary>
    public int CancelledCalls => Volatile.Read(ref _cancelledCalls);

    /// <summary>Lets every waiting call, and every later one, answer.</summary>
    public void Release() => _gate.TrySetResult();

    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => AnswerAsync(prompt, cancellationToken);

    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => AnswerAsync(string.Join('\n', messages.Select(m => m.Content)), cancellationToken);

    private async Task<LlmResponse> AnswerAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Enqueue(prompt);
        Started.TrySetResult();

        try
        {
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelledCalls);
            throw;
        }

        if (_failure is not null)
            throw new InvalidOperationException(_failure);

        return new LlmResponse { Content = _answer };
    }
}
