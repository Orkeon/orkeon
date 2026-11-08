using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>A streaming provider whose chat turn sends the given tool-call fragments, then the text "ok".</summary>
public sealed class FakeToolCallStreamingLlmProvider(params ToolCallDelta[] fragments) : ILlmProvider, IStreamingLlmProvider
{
    public string Name => "fake-tool-call-streaming";

    public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

    public bool SupportsStreaming => true;

    public bool StreamsNatively => true;

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = "ok" });

    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = "ok" });

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return "ok";
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var fragment in fragments)
            yield return LlmStreamEvent.ToolCallFragment(fragment);
        yield return LlmStreamEvent.Content("ok");
        yield return LlmStreamEvent.Complete(new LlmResponse { Content = "ok" });
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
