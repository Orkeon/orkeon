namespace Orkeon.Infrastructure.LLMs.Embeddings;

internal static class OpenAICompatibleEndpoints
{
    /// <summary>The configured endpoint, or the default, as a base URI that ends in a slash.</summary>
    public static Uri Join(string configured, string defaultEndpoint)
    {
        var root = string.IsNullOrWhiteSpace(configured) ? defaultEndpoint : configured.Trim();
        return new Uri(root.EndsWith('/') ? root : root + "/");
    }
}
