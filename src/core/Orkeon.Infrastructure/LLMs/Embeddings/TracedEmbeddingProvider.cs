using System.Diagnostics;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Opens one <c>embeddings {model}</c> Client span per call of the wrapped provider (CH-08). It sits
/// under <see cref="CachedEmbeddingProvider"/>, so a cache hit opens none.
/// </summary>
internal sealed class TracedEmbeddingProvider : IEmbeddingProvider
{
    private const string TextCountTag = "orkeon.embeddings.text_count";

    private readonly IEmbeddingProvider _inner;

    private TracedEmbeddingProvider(IEmbeddingProvider inner) => _inner = inner;

    /// <summary>
    /// Traces <paramref name="inner"/>; a provider already traced is returned as is. A cache is never
    /// wrapped: the trace goes under it (an empty cache is rebuilt over the traced provider), so a hit
    /// opens no span.
    /// </summary>
    public static IEmbeddingProvider Wrap(IEmbeddingProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner switch
        {
            TracedEmbeddingProvider => inner,
            CachedEmbeddingProvider { Inner: TracedEmbeddingProvider } => inner,
            CachedEmbeddingProvider cache => cache.WithInner(Wrap(cache.Inner)),
            _ => new TracedEmbeddingProvider(inner),
        };
    }

    internal IEmbeddingProvider Inner => _inner;

    public string Name => _inner.Name;

    public string Model => _inner.Model;

    public int Dimensions => _inner.Dimensions;

    public async System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        using var activity = Start(1);
        try
        {
            return await _inner.GetEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Fail(activity, exception);
            throw;
        }
    }

    public async System.Threading.Tasks.Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        using var activity = Start(texts.Count);
        try
        {
            return await _inner.GetEmbeddingsAsync(texts, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Fail(activity, exception);
            throw;
        }
    }

    private Activity? Start(int textCount)
    {
        var activity = OrkeonActivitySources.Llm.StartActivity(
            GenAiAttributes.SpanName(GenAiAttributes.OperationEmbeddings, _inner.Model), ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationEmbeddings);
        activity.SetTag(GenAiAttributes.ProviderName, OrkeonActivitySources.ProviderName(_inner.Name));
        activity.SetTag(GenAiAttributes.RequestModel, _inner.Model);
        activity.SetTag(TextCountTag, textCount);
        return activity;
    }

    private static void Fail(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetTag(GenAiAttributes.ErrorType, exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
    }
}
