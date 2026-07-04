namespace Orkeon.Domain.AgentCommunication;

/// <summary>Defines the type of message exchanged between agents.</summary>
public enum MessageType
{
    /// <summary>A task assignment message.</summary>
    Task,
    /// <summary>A question requiring a response.</summary>
    Question,
    /// <summary>A response to a prior question or task.</summary>
    Response,
    /// <summary>A status update message.</summary>
    Status
}
