namespace Orkeon.Domain.Attributes;

/// <summary>
/// Specialization of ComponentContract for tools. Adds tool-specific metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ToolContractAttribute : ComponentContractAttribute
{
    /// <summary>Gets or sets the category of this tool.</summary>
    public string? Category { get; set; }

    /// <summary>Initializes a new instance of <see cref="ToolContractAttribute"/> with the given unique name.</summary>
    /// <param name="uniqueName">The unique machine-readable name of the tool.</param>
    public ToolContractAttribute(string uniqueName) : base(uniqueName)
    {
    }
}
