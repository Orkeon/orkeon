namespace Orkeon.Domain.Configuration;

/// <summary>Represents differences between two configurations.</summary>
public record ConfigurationDiff
{
    private readonly List<ConfigurationChange> _changes;

    /// <summary>Gets the name of the configuration that was compared.</summary>
    public string ConfigurationName { get; }
    /// <summary>Gets the list of changes found between the two configurations.</summary>
    public IReadOnlyList<ConfigurationChange> Changes => _changes;
    /// <summary>Gets the timestamp when the comparison was performed.</summary>
    public DateTime ComparedAt { get; }
    /// <summary>Gets a value indicating whether there are any changes.</summary>
    public bool HasChanges => _changes.Count > 0;

    /// <summary>Initializes a new instance of <see cref="ConfigurationDiff"/>.</summary>
    /// <param name="configurationName">The name of the configuration.</param>
    /// <param name="changes">The list of changes.</param>
    public ConfigurationDiff(string configurationName, IReadOnlyList<ConfigurationChange>? changes = null)
    {
        ArgumentNullException.ThrowIfNull(configurationName);
        ConfigurationName = configurationName;
        _changes = changes is null ? [] : [.. changes];
        ComparedAt = DateTime.UtcNow;
    }

    /// <summary>Creates a diff with no changes.</summary>
    /// <param name="configurationName">The configuration name.</param>
    /// <returns>A <see cref="ConfigurationDiff"/> with no changes.</returns>
    public static ConfigurationDiff NoChanges(string configurationName)
    {
        return new ConfigurationDiff(configurationName);
    }

    /// <summary>Adds a change to this diff.</summary>
    /// <param name="change">The change to add.</param>
    public void AddChange(ConfigurationChange change)
    {
        _changes.Add(change);
    }
}

/// <summary>Represents a single configuration change.</summary>
public record ConfigurationChange
{
    /// <summary>Gets the dot-separated path to the changed property.</summary>
    public string PropertyPath { get; }
    /// <summary>Gets the old value, or null if the property was added.</summary>
    public object? OldValue { get; }
    /// <summary>Gets the new value, or null if the property was removed.</summary>
    public object? NewValue { get; }
    /// <summary>Gets the type of change.</summary>
    public ChangeType Type { get; }
    /// <summary>Gets an optional description of this change.</summary>
    public string? Description { get; }

    /// <summary>Initializes a new instance of <see cref="ConfigurationChange"/>.</summary>
    /// <param name="propertyPath">The property path.</param>
    /// <param name="oldValue">The old value.</param>
    /// <param name="newValue">The new value.</param>
    /// <param name="type">The change type.</param>
    /// <param name="description">Optional description.</param>
    public ConfigurationChange(
        string propertyPath,
        object? oldValue,
        object? newValue,
        ChangeType type,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(propertyPath);
        PropertyPath = propertyPath;
        OldValue = oldValue;
        NewValue = newValue;
        Type = type;
        Description = description;
    }

    /// <summary>Creates a change indicating a property was added.</summary>
    /// <param name="propertyPath">The property path.</param>
    /// <param name="value">The new value.</param>
    /// <returns>An added <see cref="ConfigurationChange"/>.</returns>
    public static ConfigurationChange Added(string propertyPath, object? value)
    {
        return new ConfigurationChange(propertyPath, null, value, ChangeType.Added);
    }

    /// <summary>Creates a change indicating a property was removed.</summary>
    /// <param name="propertyPath">The property path.</param>
    /// <param name="value">The removed value.</param>
    /// <returns>A removed <see cref="ConfigurationChange"/>.</returns>
    public static ConfigurationChange Removed(string propertyPath, object? value)
    {
        return new ConfigurationChange(propertyPath, value, null, ChangeType.Removed);
    }

    /// <summary>Creates a change indicating a property was modified.</summary>
    /// <param name="propertyPath">The property path.</param>
    /// <param name="oldValue">The old value.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns>A modified <see cref="ConfigurationChange"/>.</returns>
    public static ConfigurationChange Modified(string propertyPath, object? oldValue, object? newValue)
    {
        return new ConfigurationChange(propertyPath, oldValue, newValue, ChangeType.Modified);
    }
}
