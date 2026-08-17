using Orkeon.Infrastructure.Constants.Network;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Top-level A2A protocol configuration options.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class A2AOptions
{
    /// <summary>
    /// Whether A2A protocol support is enabled at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to run an A2A server exposing local agents for remote task submission.
    /// </summary>
    public bool EnableServer { get; set; }

    /// <summary>
    /// The port for the A2A HTTP server to listen on.
    /// </summary>
    public int Port { get; set; } = NetworkDefaults.A2APort;

    /// <summary>
    /// The host/prefix for the A2A HTTP server (e.g., "http://localhost").
    /// </summary>
    public string Host { get; set; } = "http://localhost";

    /// <summary>
    /// The agent name advertised in the agent card.
    /// </summary>
    public string AgentName { get; set; } = "Orkeon";

    /// <summary>
    /// The agent description advertised in the agent card.
    /// </summary>
    public string AgentDescription { get; set; } = "Orkeon A2A Agent";

    /// <summary>
    /// The agent version advertised in the agent card.
    /// </summary>
    public string AgentVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Organization name for the provider field in the agent card.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Contact URL for the provider field in the agent card.
    /// </summary>
    public Uri? ContactUrl { get; set; }

    /// <summary>
    /// Timeout in seconds for outbound HTTP requests to remote agents.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Known remote A2A agent endpoints to connect to.
    /// Key is the agent identifier, value is the base URL.
    /// </summary>
    public Dictionary<string, string> RemoteAgents { get; } = [];
}
