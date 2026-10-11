using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>An <see cref="ILlmProvider"/> answering every chat with <see cref="Content"/>, or failing with <see cref="Failure"/>.</summary>
internal sealed class StubTrialMailLlmProvider : ILlmProvider
{
    private int _calls;

    /// <inheritdoc />
    public string Name => "stub-trial-mail";

    /// <summary>The text of every answer.</summary>
    public string Content { get; init; } = "";

    /// <summary>Thrown by every call when set.</summary>
    public Exception? Failure { get; init; }

    /// <summary>How many calls were received.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Respond();

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Respond();

    private Task<LlmResponse> Respond()
    {
        Interlocked.Increment(ref _calls);
        if (Failure is not null)
            throw Failure;
        return Task.FromResult(new LlmResponse { Content = Content, Model = "stub" });
    }
}
