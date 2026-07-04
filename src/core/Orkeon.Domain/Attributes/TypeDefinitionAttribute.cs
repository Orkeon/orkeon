namespace Orkeon.Domain.Attributes;

/// <summary>
/// Marks a class or struct as a reusable type definition in the YAML schema.
/// Maps to the YAML 'type_definitions' section (OpenAPI aligned).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class TypeDefinitionAttribute : Attribute
{
    /// <summary>Gets the unique name of the type definition.</summary>
    public string Name { get; }
    /// <summary>Gets or sets the description of this type definition.</summary>
    public string? Description { get; set; }

    /// <summary>Initializes a new instance of <see cref="TypeDefinitionAttribute"/> with the given name.</summary>
    /// <param name="name">The unique name of the type definition.</param>
    public TypeDefinitionAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }
}
