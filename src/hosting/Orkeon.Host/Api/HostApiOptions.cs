using Orkeon.Infrastructure.AgentCommunication;

namespace Orkeon.Host.Api;

/// <summary>
/// The run API of <c>orkeon-host</c> (FWK-14), bound from <c>Orkeon:Host:Api</c>. Off by default: a daemon must
/// not open a port nobody asked for.
/// </summary>
internal sealed record HostApiOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = OrkeonHostOptions.SectionName + ":Api";

    /// <summary>The default listener prefix.</summary>
    public const string DefaultPrefix = "http://localhost:5190/";

    /// <summary>Serves the API. Off, the host listens on no port for it.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The <see cref="System.Net.HttpListener"/> prefix: scheme, host, port and a closing slash. A host the loopback does
    /// not contain (<c>http://+:5190/</c>) requires an authentication scheme or mutual TLS under <see cref="Security"/>.
    /// </summary>
    public string Prefix { get; init; } = DefaultPrefix;

    /// <summary>
    /// What a caller must present on every route but <c>/healthz</c> and <c>/readyz</c>: the schemes and the mutual-TLS
    /// trust of <see cref="HttpSecurityOptions"/>. <c>Bearer</c> needs a validator registered under
    /// <c>A2A:Security:AzureAD</c> or <c>A2A:Security:Oidc</c>; <c>ApiKey</c> reads its keys from the secrets it names.
    /// </summary>
    public HttpSecurityOptions Security { get; init; } = new();

    /// <summary>
    /// Health-check tags added to readiness besides <c>ready</c>. <c>llm</c> adds the LLM provider check, which reaches
    /// an external service: left out by default, so a provider outage does not take the daemon out of rotation.
    /// </summary>
    public IReadOnlyList<string> ReadinessTags { get; init; } = [];

    /// <summary>How long an idempotency key is remembered. After it, the same key starts a new run.</summary>
    public TimeSpan IdempotencyTtl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a quiet event stream waits before it writes a keep-alive frame (an SSE comment, a blank NDJSON line).
    /// The write is what detects a client that went away: an abandoned stream of a long run would otherwise live as long as
    /// the run.
    /// </summary>
    public TimeSpan SseKeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The most idempotency keys held in memory; the oldest finished ones go first.</summary>
    public int IdempotencyMaxKeys { get; init; } = 10_000;

    /// <summary>Whether the prefix names only the loopback interface.</summary>
    public bool ListensOnLoopbackOnly
    {
        get
        {
            if (!Uri.TryCreate(Prefix.Replace("+", "example.invalid", StringComparison.Ordinal)
                    .Replace("*", "example.invalid", StringComparison.Ordinal), UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.IsLoopback;
        }
    }

    /// <summary>Refuses a section the listener cannot honour.</summary>
    /// <exception cref="HostConfigurationException">The prefix or a bound is unusable.</exception>
    public void Validate()
    {
        if (!Prefix.EndsWith('/') || !(Prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || Prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new HostConfigurationException(
                $"{SectionName}:Prefix is '{Prefix}': write a listener prefix such as '{DefaultPrefix}' (http or https, a port, a closing slash).");
        }

        if (IdempotencyTtl <= TimeSpan.Zero)
            throw new HostConfigurationException($"{SectionName}:IdempotencyTtl must be greater than zero.");
        if (SseKeepAliveInterval <= TimeSpan.Zero)
            throw new HostConfigurationException($"{SectionName}:SseKeepAliveInterval must be greater than zero.");
        if (IdempotencyMaxKeys < 1)
            throw new HostConfigurationException($"{SectionName}:IdempotencyMaxKeys must be at least 1.");
    }
}
