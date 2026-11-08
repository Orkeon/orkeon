using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// Writes every call its inner provider answers to a cassette, and hands the answer back unchanged. A
/// call that fails with an exception is not recorded.
/// </summary>
internal sealed class RecordingLlmProvider(ILlmProvider inner, LlmCassette cassette, string profile)
    : ILlmProvider, IStreamingLlmProvider, ILlmProviderDecorator
{
    public ILlmProvider Inner => inner;

    public string Name => inner.Name;

    public LlmConfig? BaseConfig => inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => inner.Capabilities;

    public bool SupportsStreaming => (inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => (inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var response = await inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        await RecordAsync(LlmCallKey.OfPrompt(profile, prompt, config), response, null, config, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var response = await inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        await RecordAsync(LlmCallKey.Of(profile, messages, config), response, null, config, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (inner is not IStreamingLlmProvider streaming)
        {
            var response = await GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            if (response.Content.Length > 0)
                yield return response.Content;
            yield break;
        }

        var chunks = new List<LlmStreamEvent>();
        await foreach (var chunk in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
        {
            chunks.Add(LlmStreamEvent.Content(chunk));
            yield return chunk;
        }

        var text = string.Concat(chunks.Select(c => c.Delta));
        await RecordAsync(LlmCallKey.OfPrompt(profile, prompt, config), new LlmResponse { Content = text }, chunks, config, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (inner is not IStreamingLlmProvider streaming)
        {
            var response = await ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            if (response.Content.Length > 0)
                yield return LlmStreamEvent.Content(response.Content);
            yield return LlmStreamEvent.Complete(response);
            yield break;
        }

        var deltas = new List<LlmStreamEvent>();
        await foreach (var ev in streaming.ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
        {
            if (ev.Kind == LlmStreamEventKind.Completed && ev.FinalResponse is { } final)
            {
                await RecordAsync(LlmCallKey.Of(profile, messages, config), final, deltas, config, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                deltas.Add(ev);
            }

            yield return ev;
        }
    }

    private Task RecordAsync(string key, LlmResponse response, IReadOnlyList<LlmStreamEvent>? events, LlmConfig? config, CancellationToken ct) =>
        cassette.RecordAsync(profile, key, response, events, [inner.BaseConfig?.ApiKey, config?.ApiKey], ct);
}
