namespace Orkeon.Domain.Flows;

/// <summary>
/// Base attribute for flow-related metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public abstract class FlowBaseAttribute : Attribute
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the description.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Marks a method as a flow step.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FlowStepAttribute : FlowBaseAttribute
{
    /// <summary>Gets or sets the execution order.</summary>
    public int Order { get; set; }
    /// <summary>Gets or sets whether this step is optional.</summary>
    public bool IsOptional { get; set; }
    /// <summary>Gets or sets the required input names.</summary>
    public string[]? RequiredInputs { get; set; }
    /// <summary>Gets or sets the output names provided by this step.</summary>
    public string[]? ProvidedOutputs { get; set; }
}

/// <summary>
/// Marks a class as a flow.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FlowAttribute : FlowBaseAttribute
{
    /// <summary>Gets or sets the flow version.</summary>
    public string Version { get; set; } = "1.0";
    /// <summary>Gets or sets the tags associated with this flow.</summary>
    public string[]? Tags { get; set; }
}

/// <summary>
/// Marks a method as a flow input validator.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FlowValidatorAttribute : Attribute
{
    /// <summary>Gets or sets the input names this validator validates.</summary>
    public string[]? ValidatesInputs { get; set; }
}

/// <summary>
/// Marks a method as a flow error handler.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FlowErrorHandlerAttribute : Attribute
{
    /// <summary>Gets or sets the exception types this handler handles.</summary>
    public Type[]? HandlesExceptions { get; set; }
}

/// <summary>
/// Marks a method to be executed before a flow starts.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FlowBeforeAttribute : Attribute
{
}

/// <summary>
/// Marks a method to be executed after a flow completes.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FlowAfterAttribute : Attribute
{
}

/// <summary>
/// Marks a method as the starting point of a flow.
/// Equivalent to Python's @start decorator.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StartAttribute : Attribute
{
    /// <summary>
    /// Optional name for the start method. If not provided, method name is used.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// Marks a method to listen for specific events in a flow.
/// Equivalent to Python's @listen decorator.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ListenAttribute : Attribute
{
    /// <summary>
    /// The event or method name to listen for.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// Optional condition to filter events.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>Initializes a new instance of <see cref="ListenAttribute"/>.</summary>
    /// <param name="eventName">The event or method name to listen for.</param>
    public ListenAttribute(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        EventName = eventName;
    }
}

/// <summary>
/// Marks a method as a router that can direct flow to different paths.
/// Equivalent to Python's @router decorator.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RouterAttribute : Attribute
{
    /// <summary>
    /// The possible routes this router can return.
    /// </summary>
    public string[]? Routes { get; set; }

    /// <summary>
    /// Optional name for the router. If not provided, method name is used.
    /// </summary>
    public string? Name { get; set; }
}
