namespace Orkeon.Domain.Agent.Composition;

/// <summary>Represents a parameter in a template that can be customized.</summary>
public record TemplateParameter
{
    /// <summary>Gets the name of this parameter.</summary>
    public string Name { get; }
    /// <summary>Gets the description of this parameter.</summary>
    public string Description { get; }
    /// <summary>Gets the type of this parameter.</summary>
    public ParameterType Type { get; }
    /// <summary>Gets the default value, or null if no default.</summary>
    public object? DefaultValue { get; }
    /// <summary>Gets a value indicating whether this parameter is required.</summary>
    public bool IsRequired { get; }
    /// <summary>Gets the validation rules for this parameter.</summary>
    public Dictionary<string, object> ValidationRules { get; }

    /// <summary>Initializes a new instance of <see cref="TemplateParameter"/>.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="description">The parameter description.</param>
    /// <param name="type">The parameter type.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="isRequired">Whether this parameter is required.</param>
    /// <param name="validationRules">Validation rules.</param>
    public TemplateParameter(
        string name,
        string description,
        ParameterType type,
        object? defaultValue = null,
        bool isRequired = false,
        Dictionary<string, object>? validationRules = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
        Type = type;
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        ValidationRules = validationRules ?? [];
    }
}

/// <summary>Types of template parameters.</summary>
public enum ParameterType
{
    /// <summary>A textual parameter.</summary>
    Text,
    /// <summary>A numeric parameter.</summary>
    Number,
    /// <summary>A boolean parameter.</summary>
    Boolean,
    /// <summary>A date/time parameter.</summary>
    DateTime,
    /// <summary>A list parameter.</summary>
    List,
    /// <summary>A structured (object) parameter.</summary>
    Structured
}
