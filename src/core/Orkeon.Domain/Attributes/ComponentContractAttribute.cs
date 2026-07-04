namespace Orkeon.Domain.Attributes;

/// <summary>
/// Marks a class as a component with a YAML contract describing its inputs and outputs.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1813", Justification = "Base attribute type extended by ToolContractAttribute; cannot be sealed.")]
public class ComponentContractAttribute : Attribute
{
    /// <summary>Gets the unique machine-readable name of the component.</summary>
    public string UniqueName { get; }
    /// <summary>Gets or sets the human-readable display name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the description of the component.</summary>
    public string? Description { get; set; }
    /// <summary>Gets or sets the version of this contract.</summary>
    public string? Version { get; set; }
    /// <summary>Gets or sets the reference to the input schema definition.</summary>
    public string? InputSchemaRef { get; set; }
    /// <summary>Gets or sets the reference to the output schema definition.</summary>
    public string? OutputSchemaRef { get; set; }

    /// <summary>Initializes a new instance of <see cref="ComponentContractAttribute"/> with the given unique name.</summary>
    /// <param name="uniqueName">The unique machine-readable name of the component.</param>
    public ComponentContractAttribute(string uniqueName)
    {
        ArgumentNullException.ThrowIfNull(uniqueName);
        UniqueName = uniqueName;
    }
}
