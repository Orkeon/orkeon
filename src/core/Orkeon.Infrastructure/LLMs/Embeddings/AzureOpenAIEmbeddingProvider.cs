using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Azure OpenAI embeddings: <c>Model</c> is the deployment name, <c>Endpoint</c> the resource URL
/// (required), the key goes in the <c>api-key</c> header.
/// </summary>
public sealed class AzureOpenAIEmbeddingProvider : OpenAICompatibleEmbeddingProviderBase
{
    private const string DefaultApiVersion = "2024-10-21";

    /// <summary>Initializes a new instance of <see cref="AzureOpenAIEmbeddingProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="options">The embedding options.</param>
    public AzureOpenAIEmbeddingProvider(HttpClient httpClient, IOptions<EmbeddingOptions> options)
        : base(httpClient, options)
    {
    }

    /// <inheritdoc />
    public override string Name => "Azure OpenAI";

    /// <inheritdoc />
    protected override string ProviderDefaultModel => Settings.Model;

    /// <inheritdoc />
    protected override int ProviderDefaultDimension => Settings.Dimension;

    /// <inheritdoc />
    protected override Uri BuildRequestUri()
    {
        if (string.IsNullOrWhiteSpace(Settings.Endpoint))
        {
            throw new InvalidOperationException(
                "Azure OpenAI embeddings need the resource URL: set Orkeon:Embeddings:Endpoint " +
                "(https://<resource>.openai.azure.com) and Orkeon:Embeddings:Model to the deployment name.");
        }

        var version = string.IsNullOrWhiteSpace(Settings.ApiVersion) ? DefaultApiVersion : Settings.ApiVersion;
        var root = Settings.Endpoint.Trim().TrimEnd('/');
        return new Uri(
            $"{root}/openai/deployments/{Uri.EscapeDataString(Model)}/embeddings?api-version={Uri.EscapeDataString(version)}");
    }

    /// <inheritdoc />
    protected override void Authenticate(HttpRequestMessage request, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add("api-key", apiKey);
    }
}
