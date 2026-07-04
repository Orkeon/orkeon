using Orkeon.Domain.Common;

namespace Orkeon.Domain.AgentCommunication;

/// <summary>Represents a message exchanged between agents.</summary>
/// <param name="From">The identifier of the sending agent.</param>
/// <param name="To">The identifier of the receiving agent.</param>
/// <param name="Type">The type of the message.</param>
/// <param name="Content">The message content.</param>
/// <param name="Metadata">Optional metadata associated with the message.</param>
public record AgentMessage(
    AgentId From,
    AgentId To,
    MessageType Type,
    string Content,
    Dictionary<string, object>? Metadata = null
);
