using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Represents the input for crew execution.
/// </summary>
public sealed record CrewInput
{
    /// <summary>
    /// Gets the initial context for the crew execution.
    /// </summary>
    public string InitialContext { get; init; }

    /// <summary>
    /// Gets the input variables.
    /// </summary>
    public CrewVariables Variables { get; init; }

    /// <summary>
    /// Gets the input parameters (legacy support).
    /// </summary>
    public Dictionary<string, object> Parameters => Variables.ToDictionary();

    /// <summary>
    /// Gets any additional metadata.
    /// </summary>
    public CrewMetadata Metadata { get; init; }

    /// <summary>
    /// Gets when this input was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Initializes a new instance of <see cref="CrewInput"/>.</summary>
    /// <param name="initialContext">The initial context string for the crew.</param>
    /// <param name="parameters">Optional input parameters.</param>
    /// <param name="metadata">Optional metadata for this input.</param>
    internal CrewInput(
        string initialContext,
        Dictionary<string, object>? parameters = null,
        CrewMetadata? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialContext);

        InitialContext = initialContext;
        Variables = parameters != null ? CrewVariables.FromDictionary(parameters) : CrewVariables.Empty;
        Metadata = metadata ?? CrewMetadata.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a parameter value.
    /// </summary>
    public T? GetParameter<T>(string key) where T : struct
    {
        return Variables.Get<T>(key);
    }

    /// <summary>
    /// Gets a string parameter value.
    /// </summary>
    public string? GetStringParameter(string key)
    {
        return Variables.GetString(key);
    }

    /// <summary>
    /// Returns a new <see cref="CrewInput"/> with the given parameter set.
    /// </summary>
    public CrewInput WithParameter(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var updated = value switch
        {
            string str => Variables.Set(key, str),
            int intVal => Variables.Set(key, intVal),
            bool boolVal => Variables.Set(key, boolVal),
            double doubleVal => Variables.Set(key, doubleVal),
            _ => Variables.Set(key, value.ToString() ?? string.Empty)
        };

        return this with { Variables = updated };
    }
}
