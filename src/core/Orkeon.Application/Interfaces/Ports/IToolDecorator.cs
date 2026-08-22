using Orkeon.Domain.Common;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Wraps a tool at the moment it is handed to an agent. This is the seam BUS-03's
/// instrumentation lives behind: most tools enter the process through DI and are decorated
/// there, but the delegation tools are constructed per agent by
/// <c>AgentDelegationToolsProvider</c> — without this port they would silently bypass any
/// observer, and the events that matter most (a delegation, a spawn) would be the ones that
/// never fire.
/// </summary>
public interface IToolDecorator
{
    /// <summary>Returns the tool to hand out in place of <paramref name="tool"/> — possibly the same one.</summary>
    ITool Decorate(ITool tool);
}
