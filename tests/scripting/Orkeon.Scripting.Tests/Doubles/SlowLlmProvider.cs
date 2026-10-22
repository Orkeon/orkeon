using System.Collections.Concurrent;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A provider that really suspends before it answers — <c>Task.Delay(3)</c>, the shape of every
/// HTTP provider — so that a script's awaits resume on a thread-pool continuation, the way they
/// do in production. The echo provider and the stubs elsewhere in this suite complete
/// synchronously, which is why the threading family of SCR-25 never surfaced there. Answers
/// <c>R:</c> plus the prompt (or the last message).
/// A conversation whose first user message equals a key given to <see cref="Hold"/> enters its
/// call (signalled by <see cref="Entered"/>) and waits for <see cref="Release"/>; one given to
/// <see cref="FailOn"/> throws. Every chat is recorded in <see cref="Chats"/>.
/// </summary>
internal sealed class SlowLlmProvider : ILlmProvider
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _entered = new();
    private readonly ConcurrentDictionary<string, byte> _failing = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _cancelled = new();
    private int _active;
    private int _maxActive;

    public string Name => "slow";
    public LlmConfig? BaseConfig => LlmConfig.Create("slow");

    /// <summary>Every conversation handed to <c>ChatAsync</c>, in arrival order.</summary>
    public ConcurrentQueue<LlmMessage[]> Chats { get; } = new();

    /// <summary>The largest number of held conversations that were inside their call at once.</summary>
    public int MaxActive => Volatile.Read(ref _maxActive);

    /// <summary>Makes the conversation opened by <paramref name="key"/> wait for <see cref="Release"/>.</summary>
    public void Hold(string key)
    {
        _gates[key] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EnteredSource(key);
    }

    /// <summary>Completes when the held conversation of <paramref name="key"/> reached its call.</summary>
    public Task Entered(string key) => EnteredSource(key).Task;

    public void Release(string key) => _gates[key].TrySetResult();

    /// <summary>Completes when the held conversation of <paramref name="key"/> was cancelled while waiting.</summary>
    public Task Cancelled(string key) => CancelledSource(key).Task;

    private TaskCompletionSource CancelledSource(string key)
        => _cancelled.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public void FailOn(string key) => _failing[key] = 0;

    private TaskCompletionSource EnteredSource(string key)
        => _entered.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
    {
        await Task.Delay(3, ct).ConfigureAwait(false);
        return new LlmResponse { Content = "R:" + prompt };
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
    {
        Chats.Enqueue(messages);
        var key = messages.FirstOrDefault(m => m.Role == "user")?.Content;
        if (key is not null && _gates.TryGetValue(key, out var gate))
        {
            var now = Interlocked.Increment(ref _active);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxActive)) && Interlocked.CompareExchange(ref _maxActive, now, seen) != seen) { }
            EnteredSource(key).TrySetResult();
            try { await gate.Task.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { CancelledSource(key).TrySetResult(); throw; }
            finally { Interlocked.Decrement(ref _active); }
        }
        await Task.Delay(3, ct).ConfigureAwait(false);
        if (key is not null && _failing.ContainsKey(key)) throw new InvalidOperationException("boom " + key);
        return new LlmResponse { Content = "R:" + messages[^1].Content };
    }
}
