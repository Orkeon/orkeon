using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Manages tools for an Agent: add, remove, validate, and query operations.
/// Extracted from Agent to follow Single Responsibility Principle.
/// This is a domain helper (POCO), not a service — no dependency injection.
/// </summary>
internal sealed class AgentToolManager
{
    private readonly List<ITool> _tools;
    private readonly Func<ToolAccessPolicy>? _policyProvider;

    /// <param name="tools">The backing tool collection (shared with the owning <see cref="Agent"/>).</param>
    /// <param name="policyProvider">
    /// Optional accessor returning the agent's current <see cref="ToolAccessPolicy"/>.
    /// When supplied, tool addition is validated against the live policy so that a tool
    /// denied by the policy cannot be attached to the agent.
    /// </param>
    public AgentToolManager(List<ITool> tools, Func<ToolAccessPolicy>? policyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        _policyProvider = policyProvider;
    }

    /// <summary>
    /// Gets the tools as a read-only list.
    /// </summary>
    public IReadOnlyList<ITool> Tools => _tools.AsReadOnly();

    /// <summary>
    /// Adds a tool to the collection. Throws if a tool with the same name already exists
    /// or if tool permissions are invalid.
    /// </summary>
    /// <returns>The name of the added tool (for event raising).</returns>
    public string AddTool(ITool tool, string agentRole)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (_tools.Any(t => t.Name == tool.Name))
            throw new InvalidOperationException($"Tool {tool.Name} already exists on agent {agentRole}");

        // Permission validation against the agent's tool-access policy.
        if (!ValidateToolPermissions(tool))
            throw new UnauthorizedAccessException($"Agent {agentRole} lacks permissions for tool {tool.Name}");

        _tools.Add(tool);
        return tool.Name;
    }

    /// <summary>
    /// Removes a tool by name. Throws if the tool is not found.
    /// </summary>
    /// <returns>The name of the removed tool (for event raising).</returns>
    public string RemoveTool(string toolName, string agentRole)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolName);
        if (tool == null)
            throw new InvalidOperationException($"Tool {toolName} not found on agent {agentRole}");

        _tools.Remove(tool);
        return toolName;
    }

    /// <summary>
    /// Checks if the agent has a tool with the given name.
    /// </summary>
    public bool HasTool(string toolName) =>
        _tools.Any(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates that the agent's tool-access policy permits attaching the given tool.
    /// Delegates to <see cref="ToolAccessPolicy.IsToolAllowed(string)"/>. When no policy
    /// provider was supplied (legacy construction), access is unrestricted.
    /// </summary>
    /// <param name="tool">The tool whose access is being validated.</param>
    /// <returns><see langword="true"/> if the policy allows the tool; otherwise <see langword="false"/>.</returns>
    public bool ValidateToolPermissions(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var policy = _policyProvider?.Invoke();
        return policy is null || policy.IsToolAllowed(tool.Name);
    }
}
