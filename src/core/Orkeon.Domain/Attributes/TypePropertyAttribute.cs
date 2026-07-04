namespace Orkeon.Domain.Attributes;

/// <summary>
/// Describes a property within a TypeDefinition class.
/// Maps to properties within a YAML 'type_definitions' entry.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class TypePropertyAttribute : Attribute
{
    /// <summary>Gets the JSON Schema type of this property.</summary>
    public string Type { get; }
    /// <summary>Gets or sets the description of this property.</summary>
    public string? Description { get; set; }
    /// <summary>Gets or sets a reference to a named type definition.</summary>
    public string? TypeDefinitionRef { get; set; }

    /// <summary>Initializes a new instance of <see cref="TypePropertyAttribute"/> with the given type.</summary>
    /// <param name="type">The JSON Schema type of this property.</param>
    public TypePropertyAttribute(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }
}
