using System.Collections.Concurrent;
using Orkeon.Domain.Common;

namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// Maps a human-readable agent name to its <see cref="AgentId"/> so the command façade can
/// dispatch by name while the underlying <c>IAgentChannel</c> routes by id (design §3).
/// </summary>
/// <remarks>
/// Populated when an agent declares <c>onCommand</c> (the seam of §8 item 9) and consulted
/// by <c>commands.request</c>/<c>post</c>. A singleton so the agent that registers and the
/// command that dispatches share the same view, even across different Jint engines.
/// </remarks>
public sealed class AgentCommandDirectory
{
    private readonly ConcurrentDictionary<string, AgentId> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers <paramref name="name"/> → <paramref name="agentId"/>. Returns a handle that
    /// removes the mapping on dispose (only if it still points at the same id).
    /// </summary>
    public IDisposable Register(string name, AgentId agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(agentId);
        _byName[name] = agentId;
        return new Registration(this, name, agentId);
    }

    /// <summary>Resolves a name to an <see cref="AgentId"/>, or <see langword="null"/> when unknown.</summary>
    public AgentId? Resolve(string name)
        => !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var id) ? id : null;

    /// <summary>True when <paramref name="name"/> has a registered agent.</summary>
    public bool Contains(string name)
        => !string.IsNullOrEmpty(name) && _byName.ContainsKey(name);

    /// <summary>The agent names currently registered (diagnostics / error messages).</summary>
    public IReadOnlyCollection<string> GetNames() => _byName.Keys.ToList();

    private sealed class Registration(AgentCommandDirectory owner, string name, AgentId agentId) : IDisposable
    {
        public void Dispose()
        {
            // Only remove if we still own the mapping — a re-registration under the same
            // name must not be clobbered by a stale handle's dispose.
            ((ICollection<KeyValuePair<string, AgentId>>)owner._byName)
                .Remove(new KeyValuePair<string, AgentId>(name, agentId));
        }
    }
}
