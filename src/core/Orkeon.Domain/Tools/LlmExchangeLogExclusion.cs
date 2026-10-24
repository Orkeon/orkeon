namespace Orkeon.Domain.Tools;

/// <summary>
/// Keeps a named HTTP client out of the LLM exchange log. That log wraps every client the
/// <c>IHttpClientFactory</c> builds; a tool family that talks to a non-LLM API (a drive, a mailbox)
/// registers one of these for its client, so neither its traffic nor the files and messages it
/// carries are written as LLM exchanges.
/// </summary>
public sealed class LlmExchangeLogExclusion
{
    /// <summary>Excludes the client named <paramref name="clientName"/>.</summary>
    public LlmExchangeLogExclusion(string clientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ClientName = clientName;
    }

    /// <summary>The name the client is registered under (<c>AddHttpClient(name)</c>).</summary>
    public string ClientName { get; }
}
