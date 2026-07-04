using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Collaboration context value object for agent interactions.
/// </summary>
public sealed record CollaborationContext : ValueObjectRecord
{
    /// <summary>Gets the collaboration type.</summary>
    public CollaborationType Type { get; init; }
    /// <summary>Gets the set of participating agent IDs.</summary>
    public ImmutableHashSet<AgentId> Participants { get; init; }
    /// <summary>Gets the optional leader agent ID.</summary>
    public AgentId? Leader { get; init; }
    /// <summary>Gets the optional shared state document.</summary>
    public JsonDocument? SharedState { get; init; }
    /// <summary>Gets when this collaboration context was created.</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>Gets the optional maximum duration for this collaboration.</summary>
    public TimeSpan? MaxDuration { get; init; }

    /// <summary>Initializes a new <see cref="CollaborationContext"/>.</summary>
    /// <param name="type">The collaboration type.</param>
    /// <param name="participants">The participating agents (minimum 2).</param>
    /// <param name="leader">Optional leader agent.</param>
    /// <param name="sharedState">Optional shared state document.</param>
    /// <param name="maxDuration">Optional maximum duration.</param>
    private CollaborationContext(
        CollaborationType type,
        IEnumerable<AgentId> participants,
        AgentId? leader = null,
        JsonDocument? sharedState = null,
        TimeSpan? maxDuration = null)
    {
        Type = type;
        Participants = participants.ToImmutableHashSet();
        Leader = leader;
        SharedState = sharedState;
        CreatedAt = DateTime.UtcNow;
        MaxDuration = maxDuration;

        if (Participants.Count < 2)
            throw new ArgumentException("Collaboration requires at least 2 participants", nameof(participants));

        if (leader != null && !Participants.Contains(leader))
            throw new ArgumentException("Leader must be one of the participants", nameof(leader));
    }

    /// <summary>Creates a new <see cref="CollaborationContext"/>.</summary>
    /// <param name="type">The collaboration type.</param>
    /// <param name="participants">The participating agents (minimum 2).</param>
    /// <param name="leader">Optional leader agent.</param>
    /// <param name="sharedState">Optional shared state document.</param>
    /// <param name="maxDuration">Optional maximum duration.</param>
    /// <returns>A new <see cref="CollaborationContext"/>.</returns>
    public static CollaborationContext Create(
        CollaborationType type,
        IEnumerable<AgentId> participants,
        AgentId? leader = null,
        JsonDocument? sharedState = null,
        TimeSpan? maxDuration = null) =>
        new(type, participants, leader, sharedState, maxDuration);

    /// <summary>Returns whether the specified agent is a participant.</summary>
    /// <param name="agentId">The agent ID to check.</param>
    /// <returns><see langword="true"/> if the agent is a participant; otherwise <see langword="false"/>.</returns>
    public bool HasParticipant(AgentId agentId) => Participants.Contains(agentId);
    /// <summary>Gets whether this collaboration has exceeded its maximum duration.</summary>
    public bool IsExpired => MaxDuration.HasValue && DateTime.UtcNow - CreatedAt > MaxDuration.Value;
    /// <summary>Gets the number of participants.</summary>
    public int ParticipantCount => Participants.Count;

    /// <summary>Returns a new instance with the specified agent added as a participant.</summary>
    /// <param name="agentId">The agent to add.</param>
    /// <returns>A new <see cref="CollaborationContext"/> with the agent added.</returns>
    public CollaborationContext AddParticipant(AgentId agentId) =>
        this with { Participants = Participants.Add(agentId) };

    /// <summary>Returns a new instance with the specified agent removed from participants.</summary>
    /// <param name="agentId">The agent to remove.</param>
    /// <returns>A new <see cref="CollaborationContext"/> with the agent removed.</returns>
    public CollaborationContext RemoveParticipant(AgentId agentId) =>
        this with { Participants = Participants.Remove(agentId) };

    /// <inheritdoc />
    public override string ToString() =>
        $"{Type} with {ParticipantCount} participants{(Leader != null ? $" (led by {Leader})" : "")}";
}

/// <summary>Type of agent collaboration.</summary>
public sealed record CollaborationType
{
    /// <summary>Gets the string value of this collaboration type.</summary>
    public string Value { get; }
    private CollaborationType(string value) => Value = value;

    /// <summary>Peer-to-peer collaboration between agents.</summary>
    public static readonly CollaborationType PeerToPeer = new("PeerToPeer");
    /// <summary>Hierarchical collaboration with a lead agent.</summary>
    public static readonly CollaborationType Hierarchical = new("Hierarchical");
    /// <summary>Consensus-driven collaboration requiring agreement.</summary>
    public static readonly CollaborationType Consensus = new("Consensus");
    /// <summary>Broadcast collaboration from one agent to all others.</summary>
    public static readonly CollaborationType Broadcast = new("Broadcast");

    private static readonly Dictionary<string, CollaborationType> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(PeerToPeer)] = PeerToPeer,
        [nameof(Hierarchical)] = Hierarchical,
        [nameof(Consensus)] = Consensus,
        [nameof(Broadcast)] = Broadcast,
    };

    /// <summary>Gets all valid collaboration types.</summary>
    public static IReadOnlyCollection<CollaborationType> All => s_all.Values;

    /// <summary>Creates a <see cref="CollaborationType"/> from its string representation.</summary>
    public static CollaborationType From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown CollaborationType: '{value}'", nameof(value));

    /// <summary>Attempts to create a <see cref="CollaborationType"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out CollaborationType? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(CollaborationType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
