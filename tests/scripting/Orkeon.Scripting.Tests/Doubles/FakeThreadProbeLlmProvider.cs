using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A provider that records, at its synchronous entry (before any await), whether the calling thread
/// belongs to the thread pool, then suspends on a short CLR delay like <see cref="SlowLlmProvider"/>.
/// The entry runs on the thread of the JS call, so the readings tell where the engine is drained.
/// Answers <c>R:</c> plus the prompt (or the last message).
/// </summary>
internal sealed class FakeThreadProbeLlmProvider : ILlmProvider
{
    private readonly object _gate = new();

    /// <summary>One entry per call, in call order: whether the calling thread was a thread-pool thread.</summary>
    public List<bool> OnThreadPool { get; } = [];

    public string Name => "thread-probe";
    public LlmConfig? BaseConfig => LlmConfig.Create("probe");

    private void Record()
    {
        lock (_gate) OnThreadPool.Add(Thread.CurrentThread.IsThreadPoolThread);
    }

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
    {
        Record();
        await Task.Delay(3, ct).ConfigureAwait(false);
        return new LlmResponse { Content = "R:" + prompt };
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
    {
        Record();
        await Task.Delay(3, ct).ConfigureAwait(false);
        return new LlmResponse { Content = "R:" + messages[^1].Content };
    }
}
