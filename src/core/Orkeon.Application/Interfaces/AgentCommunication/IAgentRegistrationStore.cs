using Orkeon.Domain.Common;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Thread-safe, process-wide backing store for A2A agent registrations (R4.6 / ANT-001).
/// <para>
/// Decision R4.6 (QCM 2026-06-11): the A2A agent repository is <b>scoped per request</b>.
/// A scoped repository keeps nothing between two A2A requests, so it must hydrate from a
/// shared store that outlives request scopes — otherwise the A2A task router would never
/// find the agents registered by the execution pipeline. Implementations are registered
/// as <b>singletons</b>; the scoped <c>IAgentRepository</c> reads/writes through this
/// store on every call (per-request hydration).
/// </para>
/// <para>
/// All members must be safe for concurrent use: the store is shared between the A2A
/// server/router request scopes and the pipeline scopes.
/// </para>
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IAgentRegistrationStore
{
    /// <summary>Gets the number of registered agents.</summary>
    int Count { get; }

    /// <summary>Returns the agent registered under <paramref name="id"/>, or <see langword="null"/>.</summary>
    DomainAgent? GetById(AgentId id);

    /// <summary>Returns <see langword="true"/> when an agent is registered under <paramref name="id"/>.</summary>
    bool Contains(AgentId id);

    /// <summary>Returns a point-in-time snapshot of all registered agents.</summary>
    IReadOnlyList<DomainAgent> Snapshot();

    /// <summary>Registers <paramref name="agent"/> if no agent with the same id exists.</summary>
    /// <returns><see langword="true"/> when the agent was added; <see langword="false"/> when the id was already registered.</returns>
    bool TryAdd(DomainAgent agent);

    /// <summary>Registers or replaces <paramref name="agent"/> (upsert).</summary>
    void Save(DomainAgent agent);

    /// <summary>Removes the agent registered under <paramref name="id"/>.</summary>
    /// <returns><see langword="true"/> when an agent was removed.</returns>
    bool Remove(AgentId id);
}
