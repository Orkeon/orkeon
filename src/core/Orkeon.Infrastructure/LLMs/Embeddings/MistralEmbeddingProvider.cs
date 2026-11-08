using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>Mistral embeddings (<c>mistral-embed</c>, 1024 dimensions), bearer-authenticated.</summary>
public sealed class MistralEmbeddingProvider : OpenAICompatibleEmbeddingProviderBase
{
#pragma warning disable S1075 // Canonical vendor API root, a documented default that Endpoint overrides; not a filesystem path.
    private const string DefaultEndpoint = "https://api.mistral.ai/v1/";
#pragma warning restore S1075

    /// <summary>Initializes a new instance of <see cref="MistralEmbeddingProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="options">The embedding options.</param>
    public MistralEmbeddingProvider(HttpClient httpClient, IOptions<EmbeddingOptions> options)
        : base(httpClient, options)
    {
        RequireFixedDimension("mistral-embed", 1024);
    }

    /// <inheritdoc />
    public override string Name => "Mistral";

    /// <inheritdoc />
    protected override string ProviderDefaultModel => "mistral-embed";

    /// <inheritdoc />
    protected override int ProviderDefaultDimension => 1024;

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
