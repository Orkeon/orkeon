namespace Orkeon.Domain.Attributes;

/// <summary>
/// Describes a field in a component's output/return schema.
/// Type is inferred from the C# property type by default.
/// Maps to the YAML 'returns' section.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ReturnSchemaAttribute : Attribute
{
    /// <summary>
    /// JSON Schema type. If null, inferred from the C# property type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// OpenAPI format hint. If null, inferred from the C# property type.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets the description of this return field.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Example value for documentation and demo generation.
    /// </summary>
    public object? Example { get; set; }

    /// <summary>Gets or sets a reference to a named type definition.</summary>
    public string? TypeDefinitionRef { get; set; }
    /// <summary>Gets or sets the JSON Schema type for array items.</summary>
    public string? ItemsType { get; set; }
    /// <summary>Gets or sets the format for array items.</summary>
    public string? ItemsFormat { get; set; }
}
