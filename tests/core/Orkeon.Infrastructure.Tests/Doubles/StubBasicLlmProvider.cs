using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Stub for IBasicLlmProvider: the chat-client path never reads the provider, so chat is unsupported.
/// </summary>
public sealed class StubBasicLlmProvider : IBasicLlmProvider
{
    public string Name => "stub";

    public System.Threading.Tasks.Task<string> ChatAsync(
        string message, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(true);
}
