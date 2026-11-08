using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>Gemini embeddings (<c>gemini-embedding-001</c>, 3072 dimensions) through Google's OpenAI-compatible endpoint.</summary>
public sealed class GeminiEmbeddingProvider : OpenAICompatibleEmbeddingProviderBase
{
#pragma warning disable S1075 // Canonical vendor API root, a documented default that Endpoint overrides; not a filesystem path.
    private const string DefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";
#pragma warning restore S1075

    /// <summary>Initializes a new instance of <see cref="GeminiEmbeddingProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="options">The embedding options.</param>
    public GeminiEmbeddingProvider(HttpClient httpClient, IOptions<EmbeddingOptions> options)
        : base(httpClient, options)
    {
        RequireFixedDimension("gemini-embedding-001", 3072);
    }

    /// <inheritdoc />
    public override string Name => "Gemini";

    /// <inheritdoc />
    protected override string ProviderDefaultModel => "gemini-embedding-001";

    /// <inheritdoc />
    protected override int ProviderDefaultDimension => 3072;

    /// <inheritdoc />
    protected override Uri BuildRequestUri() =>
        new(OpenAICompatibleEndpoints.Join(Settings.Endpoint, DefaultEndpoint), "embeddings");

    /// <inheritdoc />
    protected override void Authenticate(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}
