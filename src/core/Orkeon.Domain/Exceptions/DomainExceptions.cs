using Orkeon.Domain.Common;

namespace Orkeon.Domain.Exceptions;

#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required

/// <summary>
/// Base domain exception.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>Initializes a new instance of <see cref="DomainException"/>.</summary>
    protected DomainException() { }
    /// <summary>Initializes a new instance of <see cref="DomainException"/>.</summary>
    /// <param name="message">The exception message.</param>
    protected DomainException(string message) : base(message) { }
    /// <summary>Initializes a new instance of <see cref="DomainException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    protected DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an agent operation fails.
/// </summary>
public sealed class AgentException : DomainException
{
    /// <summary>Gets the identifier of the agent that caused the exception.</summary>
    public AgentId AgentId { get; }

    /// <summary>Initializes a new instance of <see cref="AgentException"/>.</summary>
    public AgentException() : base() { AgentId = AgentId.Create(); }
    /// <summary>Initializes a new instance of <see cref="AgentException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public AgentException(string message) : base(message) { AgentId = AgentId.Create(); }
    /// <summary>Initializes a new instance of <see cref="AgentException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AgentException(string message, Exception innerException) : base(message, innerException) { AgentId = AgentId.Create(); }
    /// <summary>Initializes a new instance of <see cref="AgentException"/>.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="message">The exception message.</param>
    public AgentException(AgentId agentId, string message) : base(message)
    {
        AgentId = agentId;
    }

    /// <summary>Initializes a new instance of <see cref="AgentException"/> with an inner exception.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AgentException(AgentId agentId, string message, Exception innerException) : base(message, innerException)
    {
        AgentId = agentId;
    }

}

/// <summary>
/// Thrown when a task operation fails.
/// </summary>
public sealed class TaskException : DomainException
{
    /// <summary>Gets the identifier of the task that caused the exception.</summary>
    public TaskId TaskId { get; }

    /// <summary>Initializes a new instance of <see cref="TaskException"/>.</summary>
    public TaskException() : base() { TaskId = TaskId.Create(); }
    /// <summary>Initializes a new instance of <see cref="TaskException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public TaskException(string message) : base(message) { TaskId = TaskId.Create(); }
    /// <summary>Initializes a new instance of <see cref="TaskException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TaskException(string message, Exception innerException) : base(message, innerException) { TaskId = TaskId.Create(); }
    /// <summary>Initializes a new instance of <see cref="TaskException"/>.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="message">The exception message.</param>
    public TaskException(TaskId taskId, string message) : base(message)
    {
        TaskId = taskId;
    }

    /// <summary>Initializes a new instance of <see cref="TaskException"/> with an inner exception.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TaskException(TaskId taskId, string message, Exception innerException) : base(message, innerException)
    {
        TaskId = taskId;
    }

}

/// <summary>
/// Thrown when a crew operation fails.
/// </summary>
public sealed class CrewException : DomainException
{
    /// <summary>Gets the identifier of the crew that caused the exception.</summary>
    public CrewId CrewId { get; }

    /// <summary>Initializes a new instance of <see cref="CrewException"/>.</summary>
    public CrewException() : base() { CrewId = CrewId.Create(); }
    /// <summary>Initializes a new instance of <see cref="CrewException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public CrewException(string message) : base(message) { CrewId = CrewId.Create(); }
    /// <summary>Initializes a new instance of <see cref="CrewException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CrewException(string message, Exception innerException) : base(message, innerException) { CrewId = CrewId.Create(); }
    /// <summary>Initializes a new instance of <see cref="CrewException"/>.</summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="message">The exception message.</param>
    public CrewException(CrewId crewId, string message) : base(message)
    {
        CrewId = crewId;
    }

    /// <summary>Initializes a new instance of <see cref="CrewException"/> with an inner exception.</summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CrewException(CrewId crewId, string message, Exception innerException) : base(message, innerException)
    {
        CrewId = crewId;
    }

}

/// <summary>
/// Thrown when tool execution fails.
/// </summary>
public sealed class ToolExecutionException : DomainException
{
    /// <summary>Gets the name of the tool that failed.</summary>
    public string ToolName { get; }

    /// <summary>Initializes a new instance of <see cref="ToolExecutionException"/>.</summary>
    public ToolExecutionException() : base() { ToolName = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="ToolExecutionException"/>.</summary>
    /// <param name="message">The failure message.</param>
    public ToolExecutionException(string message) : base(message) { ToolName = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="ToolExecutionException"/> with an inner exception.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ToolExecutionException(string message, Exception innerException) : base(message, innerException) { ToolName = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="ToolExecutionException"/>.</summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="message">The failure message.</param>
    public ToolExecutionException(string toolName, string message) : base($"Tool '{toolName}' failed: {message}")
    {
        ToolName = toolName;
    }

    /// <summary>Initializes a new instance of <see cref="ToolExecutionException"/> with an inner exception.</summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ToolExecutionException(string toolName, string message, Exception innerException)
        : base($"Tool '{toolName}' failed: {message}", innerException)
    {
        ToolName = toolName;
    }

}

/// <summary>
/// Thrown when memory operations fail.
/// </summary>
public sealed class MemoryException : DomainException
{
    /// <summary>Initializes a new instance of <see cref="MemoryException"/>.</summary>
    public MemoryException() { }
    /// <summary>Initializes a new instance of <see cref="MemoryException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public MemoryException(string message) : base(message) { }
    /// <summary>Initializes a new instance of <see cref="MemoryException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public MemoryException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when validation fails.
/// </summary>
public sealed class ValidationException : DomainException
{
    /// <summary>Gets the name of the property that failed validation.</summary>
    public string PropertyName { get; }

    /// <summary>Initializes a new instance of <see cref="ValidationException"/>.</summary>
    public ValidationException() : base() { PropertyName = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="ValidationException"/>.</summary>
    /// <param name="message">The validation failure message.</param>
    public ValidationException(string message) : base(message) { PropertyName = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="ValidationException"/> with an inner exception.</summary>
    /// <param name="message">The validation failure message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ValidationException(string message, Exception innerException) : base(message, innerException) { PropertyName = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="ValidationException"/>.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <param name="message">The validation failure message.</param>
    public ValidationException(string propertyName, string message) : base($"Validation failed for '{propertyName}': {message}")
    {
        PropertyName = propertyName;
    }

}

/// <summary>
/// Thrown when a resource is not found.
/// </summary>
public sealed class NotFoundException : DomainException
{
    /// <summary>Gets the type of the resource that was not found.</summary>
    public string ResourceType { get; }
    /// <summary>Gets the identifier of the resource that was not found.</summary>
    public string ResourceId { get; }

    /// <summary>Initializes a new instance of <see cref="NotFoundException"/>.</summary>
    public NotFoundException() : base() { ResourceType = string.Empty; ResourceId = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="NotFoundException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public NotFoundException(string message) : base(message) { ResourceType = string.Empty; ResourceId = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="NotFoundException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { ResourceType = string.Empty; ResourceId = string.Empty; }
    /// <summary>Initializes a new instance of <see cref="NotFoundException"/>.</summary>
    /// <param name="resourceType">The resource type name.</param>
    /// <param name="resourceId">The resource identifier.</param>
    public NotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} with ID '{resourceId}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

}

/// <summary>
/// Thrown when an operation times out.
/// </summary>
public sealed class TimeoutException : DomainException
{
    /// <summary>Gets the timeout duration that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Initializes a new instance of <see cref="TimeoutException"/>.</summary>
    public TimeoutException() : base() { }
    /// <summary>Initializes a new instance of <see cref="TimeoutException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public TimeoutException(string message) : base(message) { }
    /// <summary>Initializes a new instance of <see cref="TimeoutException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TimeoutException(string message, Exception innerException) : base(message, innerException) { }
    /// <summary>Initializes a new instance of <see cref="TimeoutException"/>.</summary>
    /// <param name="operation">The name of the operation that timed out.</param>
    /// <param name="timeout">The timeout duration.</param>
    public TimeoutException(string operation, TimeSpan timeout)
        : base($"Operation '{operation}' timed out after {timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds.")
    {
        Timeout = timeout;
    }

}

#pragma warning restore S3925
