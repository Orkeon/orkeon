using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A provider that does what its fields say: answers, answers with a failure, throws, or streams
/// events and then fails. Plain fields configure it, <see cref="Calls"/> says how often it was reached.
/// </summary>
public sealed class FakeScriptedLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    public string Name { get; set; } = "fake";
    public LlmProviderCapabilities Capabilities { get; set; } = LlmProviderCapabilities.Unknown;

    /// <summary>What the buffered calls answer.</summary>
    public LlmResponse Response = new() { Content = "ok" };

    /// <summary>When set, the buffered calls throw it.</summary>
    public Exception? Throws;

    /// <summary>Runs on every call, before it answers.</summary>
    public Action? OnCall;

    /// <summary>The events <c>ChatStreamingAsync</c> yields.</summary>
    public IReadOnlyList<LlmStreamEvent> StreamEvents = [];

    /// <summary>The chunks <c>GenerateStreamingAsync</c> yields.</summary>
    public IReadOnlyList<string> Chunks = [];

    /// <summary>Thrown by a stream before it yields anything.</summary>
    public Exception? StreamThrowsBefore;

    /// <summary>Thrown by a stream once it yielded everything it holds.</summary>
    public Exception? StreamThrowsAfter;

    /// <summary>How many calls reached the provider, buffered or streamed.</summary>
    public int Calls;

    /// <summary>The configuration the last call carried.</summary>
    public LlmConfig? LastConfig;

    /// <summary>The configuration the provider declares (<see cref="ILlmProvider.BaseConfig"/>).</summary>
    public LlmConfig? BaseConfig { get; set; }

    public bool SupportsStreaming => true;

    /// <summary>A failed answer: the provider's own account of it, and its error type.</summary>
    public static LlmResponse Failed(string error, string errorType) => new()
    {
        Metadata = new Dictionary<string, object>
        {
            [LlmResponseMetadataKeys.Error] = error,
            [LlmResponseMetadataKeys.ErrorType] = errorType,
        },
    };

    /// <summary>The failure an OpenAI-compatible provider reports for an HTTP status.</summary>
    public static LlmResponse Http(System.Net.HttpStatusCode status) =>
        Failed($"Fake API error: {status} - the vendor said no", "APIError");

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) => Answer(config);

    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) => Answer(config);

    private Task<LlmResponse> Answer(LlmConfig? config)
    {
        Calls++;
        LastConfig = config;
        OnCall?.Invoke();
        return Throws is null ? Task.FromResult(Response) : Task.FromException<LlmResponse>(Throws);
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        LastConfig = config;
        OnCall?.Invoke();
        await System.Threading.Tasks.Task.Yield();
        if (StreamThrowsBefore is not null)
            throw StreamThrowsBefore;
        foreach (var chunk in Chunks)
            yield return chunk;
        if (StreamThrowsAfter is not null)
            throw StreamThrowsAfter;
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        LastConfig = config;
        OnCall?.Invoke();
        await System.Threading.Tasks.Task.Yield();
        if (StreamThrowsBefore is not null)
            throw StreamThrowsBefore;
        foreach (var ev in StreamEvents)
            yield return ev;
        if (StreamThrowsAfter is not null)
            throw StreamThrowsAfter;
    }
}
