namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown to a waiter on <c>queue.pop()</c> when another agent calls
/// <c>queue.kick()</c> to wake all waiters before a value arrives.
/// </summary>
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
}
