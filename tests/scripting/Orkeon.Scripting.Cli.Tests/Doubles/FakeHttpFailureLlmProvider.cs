using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>A provider whose every call fails the way an overloaded vendor does: a 503.</summary>
internal sealed class FakeHttpFailureLlmProvider : ILlmProvider
{
    private int _calls;

    /// <inheritdoc />
    public string Name => "fake-down";

    /// <summary>How many calls reached the provider.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Fail());

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Fail());

    private LlmResponse Fail()
    {
        Interlocked.Increment(ref _calls);
        return new LlmResponse
        {
            Metadata = new Dictionary<string, object>
            {
                [LlmResponseMetadataKeys.Error] = "Fake API error: ServiceUnavailable - overloaded",
                [LlmResponseMetadataKeys.ErrorType] = "APIError",
            },
        };
    }
}
