namespace Orkeon.Infrastructure.Logging;

/// <summary>Keys of the <see cref="HttpRequestMessage.Options"/> read by <see cref="LlmLoggingDelegatingHandler"/>.</summary>
internal static class LlmExchangeOptions
{
    /// <summary>Holder in which a provider writes the answer it rebuilt from a streamed exchange.</summary>
    internal static readonly HttpRequestOptionsKey<LlmExchangeCapture> Rebuilt = new("Orkeon.LlmExchange.Rebuilt");
}

/// <summary>The rebuilt answer of a streamed exchange, written by the provider, read by the logging handler.</summary>
internal sealed class LlmExchangeCapture
{
    /// <summary>The rebuilt response body, or null when nothing was rebuilt.</summary>
    public string? Body { get; set; }
}
