using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// A DelegatingChatClient that wraps IChatClient calls with OpenTelemetry tracing spans
/// and records token usage metrics.
/// </summary>
public class OrkeonTelemetryChatClient : DelegatingChatClient
{
    private readonly OrkeonMetrics? _metrics;
    private readonly string _providerName;

    /// <summary>
    /// Creates a new OrkeonTelemetryChatClient wrapping the given inner client.
    /// </summary>
    /// <param name="innerClient">The inner IChatClient to wrap.</param>
    /// <param name="metrics">Optional metrics recorder. When null, only tracing is emitted.</param>
    /// <param name="providerName">The provider name used for tracing tags. Defaults to "unknown".</param>
    public OrkeonTelemetryChatClient(
        IChatClient innerClient,
        OrkeonMetrics? metrics = null,
        string providerName = "unknown")
        : base(innerClient)
    {
        _metrics = metrics;
        _providerName = providerName;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.ModelId ?? "unknown";
        using var activity = TracingInstrumentation.StartLlmCall(_providerName, model);
        _metrics?.LlmCallStarted(_providerName);

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

            var promptTokens = response.Usage?.InputTokenCount ?? 0;
            var completionTokens = response.Usage?.OutputTokenCount ?? 0;

            TracingInstrumentation.CompleteLlmCall(
                activity,
                promptTokens: (int)promptTokens,
                completionTokens: (int)completionTokens,
                success: true);

            _metrics?.RecordLlmCall(
                _providerName,
                model,
                durationMs: activity?.Duration.TotalMilliseconds ?? 0,
                promptTokens: (int)promptTokens,
                completionTokens: (int)completionTokens);

            return response;
        }
        catch (Exception ex)
        {
            TracingInstrumentation.CompleteLlmCall(activity, success: false);
            TracingInstrumentation.RecordException(activity, ex);
            throw;
        }
        finally
        {
            _metrics?.LlmCallCompleted(_providerName);
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = options?.ModelId ?? "unknown";
        var activity = TracingInstrumentation.StartLlmCall(_providerName, model);
        _metrics?.LlmCallStarted(_providerName);

        ChatResponseUpdate? lastUpdate = null;
        var error = false;

        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                lastUpdate = update;
                yield return update;
            }
        }
        finally
        {
            if (!error)
            {
                TracingInstrumentation.CompleteLlmCall(activity, success: true);
            }

            _metrics?.LlmCallCompleted(_providerName);
            activity?.Dispose();
        }
    }
}
