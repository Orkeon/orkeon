using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// Writes every call its inner provider answers to a cassette, and hands the answer back unchanged. A
/// call that fails with an exception is recorded as that failure, so a replay raises the same kind of
/// exception; the caller's own cancellation is not a failure of the model and is not recorded.
/// </summary>
internal sealed class RecordingLlmProvider(ILlmProvider inner, LlmCassette cassette, string profile)
    : ILlmProvider, IStreamingLlmProvider, ILlmProviderDecorator
{
    public ILlmProvider Inner => inner;

    public string Name => inner.Name;

    public LlmConfig? BaseConfig => inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => inner.Capabilities;

    /// <inheritdoc/>
    public LlmReasoningCapabilities? DescribeReasoning(string? model) => inner.DescribeReasoning(model);

    /// <inheritdoc/>
    public LlmEffortReport? ReportEffort(LlmConfig? config) => inner.ReportEffort(config);

    public bool SupportsStreaming => (inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => (inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var key = LlmCallKey.OfPrompt(profile, prompt, config);
        LlmResponse response;
        try
        {
            response = await inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsModelFailure(ex, cancellationToken))
        {
            await RecordAsync(key, null, null, config, cancellationToken, ex).ConfigureAwait(false);
            throw;
        }

        await RecordAsync(key, response, null, config, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var key = LlmCallKey.Of(profile, messages, config);
        LlmResponse response;
        try
        {
            response = await inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsModelFailure(ex, cancellationToken))
        {
            await RecordAsync(key, null, null, config, cancellationToken, ex).ConfigureAwait(false);
            throw;
        }

        await RecordAsync(key, response, null, config, cancellationToken).ConfigureAwait(false);
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

        var key = LlmCallKey.OfPrompt(profile, prompt, config);
        var chunks = new List<LlmStreamEvent>();
        var enumerator = streaming.GenerateStreamingAsync(prompt, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var disposal = enumerator.ConfigureAwait(false);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    break;
            }
            catch (Exception ex) when (IsModelFailure(ex, cancellationToken))
            {
                await RecordAsync(key, null, chunks, config, cancellationToken, ex).ConfigureAwait(false);
                throw;
            }

            chunks.Add(LlmStreamEvent.Content(enumerator.Current));
            yield return enumerator.Current;
        }

        var text = string.Concat(chunks.Select(c => c.Delta));
        await RecordAsync(key, new LlmResponse { Content = text }, chunks, config, cancellationToken).ConfigureAwait(false);
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

        var key = LlmCallKey.Of(profile, messages, config);
        var deltas = new List<LlmStreamEvent>();
        var enumerator = streaming.ChatStreamingAsync(messages, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var disposal = enumerator.ConfigureAwait(false);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    break;
            }
            catch (Exception ex) when (IsModelFailure(ex, cancellationToken))
            {
                await RecordAsync(key, null, deltas, config, cancellationToken, ex).ConfigureAwait(false);
                throw;
            }

            var ev = enumerator.Current;
            if (ev.Kind == LlmStreamEventKind.Completed && ev.FinalResponse is { } final)
                await RecordAsync(key, final, deltas, config, cancellationToken).ConfigureAwait(false);
            else
                deltas.Add(ev);

            yield return ev;
        }
    }

    private static bool IsModelFailure(Exception ex, CancellationToken ct) =>
        !(ex is OperationCanceledException && ct.IsCancellationRequested);

    private Task RecordAsync(
        string key, LlmResponse? response, IReadOnlyList<LlmStreamEvent>? events, LlmConfig? config, CancellationToken ct,
        Exception? failure = null) =>
        cassette.RecordAsync(profile, key, response, events, [inner.BaseConfig?.ApiKey, config?.ApiKey], ct, failure);
}
