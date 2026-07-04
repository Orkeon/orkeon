using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Hand-rolled <see cref="ILlmProvider"/> stub used by tests that previously relied on
/// <c>Mock&lt;ILlmProvider&gt;</c>. Captures every Generate/Chat call and returns a
/// configurable response.
/// </summary>
public sealed class StubLlmProvider : ILlmProvider
{
    private Func<string, LlmResponse> _generateResponder = _ => new LlmResponse { Content = string.Empty };
    private Func<LlmMessage[], LlmResponse> _chatResponder = _ => new LlmResponse { Content = string.Empty };

    /// <inheritdoc />
    public string Name { get; init; } = "stub";

    /// <summary>Captured prompts from <see cref="GenerateAsync"/>.</summary>
    public List<string> GenerateCalls { get; } = new();

    /// <summary>Captured message sets from <see cref="ChatAsync"/>.</summary>
    public List<LlmMessage[]> ChatCalls { get; } = new();

    /// <summary>The last <see cref="LlmConfig"/> received (Generate or Chat). Null until the first call.</summary>
    public LlmConfig? LastConfig { get; private set; }

    /// <summary>Configures the response returned by <see cref="GenerateAsync"/>.</summary>
    public StubLlmProvider RespondTo(Func<string, LlmResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _generateResponder = responder;
        return this;
    }

    /// <summary>Sets a constant <see cref="GenerateAsync"/> response.</summary>
    public StubLlmProvider RespondWith(LlmResponse response)
    {
        _generateResponder = _ => response;
        return this;
    }

    /// <summary>Sets a constant <see cref="ChatAsync"/> response.</summary>
    public StubLlmProvider RespondToChatWith(LlmResponse response)
    {
        _chatResponder = _ => response;
        return this;
    }

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        GenerateCalls.Add(prompt);
        LastConfig = config;
        return Task.FromResult(_generateResponder(prompt));
    }

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ChatCalls.Add(messages);
        LastConfig = config;
        return Task.FromResult(_chatResponder(messages));
    }
}
