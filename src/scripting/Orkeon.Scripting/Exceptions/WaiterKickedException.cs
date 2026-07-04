using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown to a waiter on <c>queue.pop()</c> when another agent calls
/// <c>queue.kick()</c> to wake all waiters before a value arrives.
/// </summary>
[Serializable]
public sealed class WaiterKickedException : Exception
{
    /// <summary>Name of the queue whose waiters were kicked.</summary>
    public string QueueName { get; }

    /// <inheritdoc />
    public WaiterKickedException(string queueName)
        : base($"Waiter on queue '{queueName}' was kicked.")
    {
        QueueName = queueName;
    }

    /// <inheritdoc />
    public WaiterKickedException()
    {
        QueueName = string.Empty;
    }

    /// <inheritdoc />
    public WaiterKickedException(string message, Exception innerException) : base(message, innerException)
    {
        QueueName = string.Empty;
    }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private WaiterKickedException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        QueueName = string.Empty;
    }
#pragma warning restore SYSLIB0051
}
