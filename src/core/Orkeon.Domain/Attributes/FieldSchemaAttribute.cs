namespace Orkeon.Domain.Attributes;

/// <summary>
/// Describes a parameter field in a component's input schema.
/// Type and Required are inferred from the C# property type by default.
/// Maps to the YAML 'parameters' section.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class FieldSchemaAttribute : Attribute
{
    /// <summary>
    /// JSON Schema type (string, integer, number, boolean, array, object).
    /// If null, inferred from the C# property type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// OpenAPI format hint (int32, int64, float, double, date-time, uuid, etc.).
    /// If null, inferred from the C# property type.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Human-readable description of the parameter.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the parameter is required.
    /// If not set (null), inferred from C# nullability (non-nullable = required, nullable = optional).
    /// Use IsRequired property to set explicitly from attributes.
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// Attribute-friendly setter for Required. Use this in [FieldSchema(IsRequired = false)].
    /// Maps to the nullable Required property internally.
    /// </summary>
    public bool IsRequired
    {
        get => Required ?? true;
        set => Required = value;
    }

    /// <summary>Gets or sets the default value for this parameter.</summary>
    public object? Default { get; set; }
    /// <summary>Gets or sets the allowed enumeration values.</summary>
    public string[]? Enum { get; set; }

    /// <summary>
    /// Example value for documentation and demo generation.
    /// Used by ToolSchema.GenerateExample() to produce a complete usage sample.
    /// </summary>
    public object? Example { get; set; }

    /// <summary>Gets or sets a reference to a named type definition.</summary>
    public string? TypeDefinitionRef { get; set; }
    /// <summary>Gets or sets the JSON Schema type for array items.</summary>
    public string? ItemsType { get; set; }
    /// <summary>Gets or sets the format for array items.</summary>
    public string? ItemsFormat { get; set; }
}
