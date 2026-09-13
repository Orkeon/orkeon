using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A provider that really suspends before it answers — <c>Task.Delay(3)</c>, the shape of every
/// HTTP provider — so that a script's awaits resume on a thread-pool continuation, the way they
/// do in production. The echo provider and the stubs elsewhere in this suite complete
/// synchronously, which is why the threading family of SCR-25 never surfaced there. Answers
/// <c>R:</c> plus the prompt (or the last message).
/// </summary>
internal sealed class SlowLlmProvider : ILlmProvider
{
    public string Name => "slow";
    public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "slow" };

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
    {
        await Task.Delay(3, ct).ConfigureAwait(false);
        return new LlmResponse { Content = "R:" + prompt };
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
    {
        await Task.Delay(3, ct).ConfigureAwait(false);
        return new LlmResponse { Content = "R:" + messages[^1].Content };
    }
}
