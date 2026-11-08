using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// Answers every call from a cassette. Its inner provider only says what it is (name, capabilities,
/// streaming) and is never called: a call the cassette does not hold fails with a
/// <see cref="ReplayMissException"/>, it does not reach the network.
/// </summary>
internal sealed class ReplayLlmProvider(ILlmProvider inner, LlmCassette cassette, string profile)
    : ILlmProvider, IStreamingLlmProvider, ILlmProviderDecorator
{
    public ILlmProvider Inner => inner;

    public string Name => inner.Name;

    public LlmConfig? BaseConfig => inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => inner.Capabilities;

    public bool SupportsStreaming => (inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => (inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        (await cassette.TakeAsync(profile, LlmCallKey.OfPrompt(profile, prompt, config), Excerpt(prompt), cancellationToken).ConfigureAwait(false)).Response;

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        (await cassette.TakeAsync(profile, LlmCallKey.Of(profile, messages, config), LlmCallKey.Excerpt(messages), cancellationToken).ConfigureAwait(false)).Response;

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (response, events) = await cassette.TakeAsync(profile, LlmCallKey.OfPrompt(profile, prompt, config), Excerpt(prompt), cancellationToken).ConfigureAwait(false);
        if (response.Error is { } error)
            throw new InvalidOperationException(error);
        if (events is null)
        {
            if (response.Content.Length > 0)
                yield return response.Content;
            yield break;
        }

        foreach (var ev in events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta && e.Delta is not null))
            yield return ev.Delta!;
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (response, events) = await cassette.TakeAsync(profile, LlmCallKey.Of(profile, messages, config), LlmCallKey.Excerpt(messages), cancellationToken).ConfigureAwait(false);
        if (events is null)
        {
            if (response.Error is null && response.Content.Length > 0)
                yield return LlmStreamEvent.Content(response.Content);
        }
        else
        {
            foreach (var ev in events)
                yield return ev;
        }

        yield return LlmStreamEvent.Complete(response);
    }

    private static string Excerpt(string prompt) => LlmCallKey.Excerpt([new LlmMessage { Role = "user", Content = prompt ?? string.Empty }]);
}
