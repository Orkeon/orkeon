using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.AgentCommunication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Constants.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Discovers remote A2A agents by fetching their agent cards
/// from the well-known endpoint (<c>/.well-known/agent.json</c>).
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class A2AAgentDiscovery : IA2AAgentDiscovery
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>Initializes a new instance of <see cref="A2AAgentDiscovery"/>.</summary>
    public A2AAgentDiscovery(
        IHttpClientFactory httpClientFactory,
        ILogger<A2AAgentDiscovery>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<A2AAgentDiscovery>.Instance;
    }

    /// <inheritdoc />
    public Task<AgentCard?> DiscoverAsync(Uri agentUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentUrl);

        return DiscoverCoreAsync();

        async Task<AgentCard?> DiscoverCoreAsync()
        {
            var url = agentUrl.ToString().TrimEnd('/') + "/.well-known/agent.json";
            var requestUri = new Uri(url, UriKind.Absolute);

            try
            {
                using var client = _httpClientFactory.CreateClient("A2A");
                using var response = await client.GetAsync(requestUri, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    LogDiscoveryFailed(url, (int)response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var card = JsonSerializer.Deserialize<AgentCard>(json, JsonOptions);

                LogDiscoveredAgent(card?.Name, agentUrl, card?.Skills.Count ?? 0);

                return card;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                LogDiscoveryException(ex, url);
                return null;
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentCard>> DiscoverMultipleAsync(
        IEnumerable<string> agentUrls, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentUrls);

        return DiscoverMultipleAsyncCore(agentUrls, ct);
    }

    private async Task<IReadOnlyList<AgentCard>> DiscoverMultipleAsyncCore(
        IEnumerable<string> agentUrls, CancellationToken ct)
    {
        var tasks = agentUrls.Select(url => DiscoverAsync(new Uri(url), ct));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return results.Where(card => card != null).Cast<AgentCard>().ToList();
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A agent discovery failed for {Url}: HTTP {StatusCode}")]
    private partial void LogDiscoveryFailed(string url, int statusCode);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Discovered A2A agent {Name} at {Url} with {SkillCount} skills")]
    private partial void LogDiscoveredAgent(string? name, Uri url, int skillCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "A2A agent discovery failed for {Url}")]
    private partial void LogDiscoveryException(Exception ex, string url);
}
