using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Interop.AgentFramework;

/// <summary>Request of <see cref="AIAgentTool"/>: what to ask the wrapped agent.</summary>
public sealed class AIAgentToolRequest
{
    /// <summary>The request handed to the Microsoft Agent Framework agent, as one user message.</summary>
    [JsonPropertyName("request")]
    [FieldSchema(Description = "What to ask the delegated agent", Example = "Summarise the three main risks of this plan", IsRequired = true)]
    public string Request { get; set; } = string.Empty;
}

/// <summary>Response of <see cref="AIAgentTool"/>: the agent's answer.</summary>
public sealed class AIAgentToolResponse
{
    /// <summary>The text the agent answered with.</summary>
    [JsonPropertyName("answer")]
    [ReturnSchema(Description = "The delegated agent's answer")]
    public string Answer { get; init; } = string.Empty;

    /// <summary>The name of the agent that answered.</summary>
    [JsonPropertyName("agent")]
    [ReturnSchema(Description = "The delegated agent's name")]
    public string Agent { get; init; } = string.Empty;
}

/// <summary>
/// A Microsoft Agent Framework <see cref="AIAgent"/> exposed as an Orkeon tool, so any
/// Orkeon agent can delegate to it the way it delegates to any other tool -- the mirror of
/// MAF's <c>AsAIFunction()</c>. One tool instance keeps one MAF session: successive calls
/// from the same crew run continue the same conversation on the MAF side.
/// </summary>
public sealed class AIAgentTool : ToolBase<AIAgentToolRequest, AIAgentToolResponse>
{
    private readonly AIAgent _agent;
    private readonly string _name;
    private readonly string _description;
    private readonly Lazy<Task<AgentSession>> _session;

    /// <summary>Wraps <paramref name="agent"/> under the tool name <paramref name="toolName"/>.</summary>
    /// <param name="agent">The MAF agent to delegate to.</param>
    /// <param name="toolName">The name the Orkeon agent calls it by; <c>agent_&lt;name&gt;</c> by default.</param>
    /// <param name="description">What the tool does; the MAF agent's description by default.</param>
    /// <param name="logger">Optional logger.</param>
    public AIAgentTool(AIAgent agent, string? toolName = null, string? description = null, ILogger<AIAgentTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        _name = string.IsNullOrWhiteSpace(toolName) ? "agent_" + Slug(agent.Name ?? agent.Id) : toolName;
        _description = string.IsNullOrWhiteSpace(description)
            ? (agent.Description ?? $"Delegate a request to the {agent.Name ?? agent.Id} agent and get its answer")
            : description;
        _session = new(() => _agent.CreateSessionAsync(CancellationToken.None).AsTask(), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override string Category => "Delegation";

    /// <inheritdoc />
    protected override async Task<AIAgentToolResponse> ExecuteTypedAsync(AIAgentToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await _session.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        var response = await _agent.RunAsync(request.Request, session, options: null, cancellationToken).ConfigureAwait(false);
        return new AIAgentToolResponse { Answer = response.Text, Agent = _agent.Name ?? _agent.Id };
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(AIAgentToolRequest request) =>
        string.IsNullOrWhiteSpace(request?.Request) ? "Required parameter 'request' is missing" : null;

#pragma warning disable CA1308 // a tool name is lower-case by convention
    private static string Slug(string value) =>
        new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
#pragma warning restore CA1308
}
