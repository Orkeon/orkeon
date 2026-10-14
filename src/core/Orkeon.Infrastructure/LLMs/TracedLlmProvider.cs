using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Opens one <c>chat {model}</c> Client span per model call (CH-08), whoever makes the call. It
/// opens none when the current activity is already a chat span - an agent loop turn, a script call,
/// a provider called inside another - so a call is one chat, however many layers it crosses.
/// </summary>
internal sealed class TracedLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private const string LlmOperationTag = "orkeon.llm.operation";

    private readonly ILlmProvider _inner;

    private TracedLlmProvider(ILlmProvider inner) => _inner = inner;

    /// <summary>Traces <paramref name="inner"/>; a provider already traced is returned as is.</summary>
    public static ILlmProvider Wrap(ILlmProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner is TracedLlmProvider ? inner : new TracedLlmProvider(inner);
    }

    /// <summary>The provider under the trace, for type inspection only.</summary>
    internal ILlmProvider Inner => _inner;

    public string Name => _inner.Name;

    public LlmConfig? BaseConfig => _inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => _inner.Capabilities;

    public bool SupportsStreaming => (_inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => (_inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        using var activity = Start(config);
        try
        {
            var response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            Complete(activity, response);
            return response;
        }
        catch (Exception ex)
        {
            Fail(activity, ex);
            throw;
        }
    }

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        using var activity = Start(config);
        try
        {
            var response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            Complete(activity, response);
            return response;
        }
        catch (Exception ex)
        {
            Fail(activity, ex);
            throw;
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = Start(config);
        if (_inner is not IStreamingLlmProvider streaming)
        {
            LlmResponse response;
            try
            {
                response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Fail(activity, ex);
                throw;
            }

            Complete(activity, response);
            if (!string.IsNullOrEmpty(response.Content))
                yield return response.Content;
            else if (response.Error is { } error)
                throw new HttpRequestException($"{error}: the buffered fallback answered with no content, so the stream carries nothing.");
            yield break;
        }

        var enumerator = streaming.GenerateStreamingAsync(prompt, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    yield break;
            }
            catch (Exception ex)
            {
                Fail(activity, ex);
                throw;
            }

            yield return enumerator.Current;
        }
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = Start(config);
        if (_inner is not IStreamingLlmProvider streaming)
        {
            LlmResponse response;
            try
            {
                response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Fail(activity, ex);
                throw;
            }

            Complete(activity, response);
            if (!string.IsNullOrEmpty(response.Content))
                yield return LlmStreamEvent.Content(response.Content);
            yield return LlmStreamEvent.Complete(response);
            yield break;
        }

        var enumerator = streaming.ChatStreamingAsync(messages, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
        while (true)
        {
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    yield break;
            }
            catch (Exception ex)
            {
                Fail(activity, ex);
                throw;
            }

            var streamed = enumerator.Current;
            if (streamed is { Kind: LlmStreamEventKind.Completed, FinalResponse: { } final })
                Complete(activity, final);
            yield return streamed;
        }
    }

    private Activity? Start(LlmConfig? config)
    {
        if (Activity.Current?.GetTagItem(GenAiAttributes.OperationName) is string current
            && current == GenAiAttributes.OperationChat)
            return null;

        var model = string.IsNullOrWhiteSpace(config?.Model) ? _inner.BaseConfig?.Model : config.Model;
        var activity = OrkeonActivitySources.Llm.StartActivity(
            GenAiAttributes.SpanName(GenAiAttributes.OperationChat, model), ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationChat);
        activity.SetTag(GenAiAttributes.ProviderName, OrkeonActivitySources.ProviderName(_inner.Name));
        activity.SetTag(GenAiAttributes.RequestModel, model);
        if (config?.Temperature is { } temperature)
            activity.SetTag(GenAiAttributes.RequestTemperature, temperature);
        if (config?.MaxTokens is { } maxTokens)
            activity.SetTag(GenAiAttributes.RequestMaxTokens, maxTokens);

        var attribution = LlmUsageScope.Current;
        activity.SetTag(LlmOperationTag, attribution.Operation);
        if (attribution.AgentId.Length > 0)
            activity.SetTag(GenAiAttributes.AgentName, attribution.AgentId);
        if (attribution.TaskId.Length > 0)
            activity.SetTag(OrkeonRunSpans.TaskId, attribution.TaskId);
        return activity;
    }

    private static void Complete(Activity? activity, LlmResponse response)
    {
        if (activity is null)
            return;

        activity.SetTag(GenAiAttributes.UsageInputTokens, response.PromptTokens);
        activity.SetTag(GenAiAttributes.UsageOutputTokens, response.CompletionTokens);
        if (response.Error is { } error)
        {
            activity.SetTag(
                GenAiAttributes.ErrorType, string.IsNullOrWhiteSpace(response.ErrorType) ? "_OTHER" : response.ErrorType);
            activity.SetStatus(ActivityStatusCode.Error, error);
        }
    }

    private static void Fail(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetTag(GenAiAttributes.ErrorType, exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }
}
