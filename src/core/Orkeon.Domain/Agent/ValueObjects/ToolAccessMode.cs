namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Defines the mode of tool access control for an agent.
/// </summary>
public enum ToolAccessMode
{
    /// <summary>
    /// No restrictions. The agent can access all available tools.
    /// </summary>
    Unrestricted = 0,

    /// <summary>
    /// Whitelist mode. The agent can only access tools explicitly listed in the whitelist.
    /// If the whitelist is empty, no tools are accessible.
    /// </summary>
    Whitelist = 1,

    /// <summary>
    /// Blacklist mode. The agent can access all tools except those explicitly listed in the blacklist.
    /// </summary>
    Blacklist = 2
}
