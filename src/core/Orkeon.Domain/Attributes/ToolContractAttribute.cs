namespace Orkeon.Domain.Attributes;

/// <summary>
/// Specialization of ComponentContract for tools. Adds tool-specific metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ToolContractAttribute : ComponentContractAttribute
{
    /// <summary>Gets or sets the category of this tool.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets whether the tool acts on the workspace of the Orkeon that runs it: its files, its shell,
    /// its code index (#445). A tool bridge never selects such a tool through <c>*</c>; naming it explicitly
    /// bridges it, and it then acts on the bridge machine. Read through wrappers with
    /// <see cref="Orkeon.Domain.Tools.ToolContracts.ActsOnWorkspace"/>.
    /// </summary>
    public bool ActsOnWorkspace { get; set; }

    /// <summary>
    /// Gets or sets whether the tool belongs to the run of the Orkeon that calls it (#445): its human, its memory, its
    /// session, its budget, its sub-agents, its decision model, its progress, its event hub, its RAG stores. Run on
    /// another machine, it would act on that machine's state, not the caller's. A tool bridge never selects such a tool
    /// through <c>*</c>, and naming it refuses the start. Read through wrappers with
    /// <see cref="Orkeon.Domain.Tools.ToolContracts.StaysWithCaller"/>.
    /// </summary>
    public bool StaysWithCaller { get; set; }

    /// <summary>Initializes a new instance of <see cref="ToolContractAttribute"/> with the given unique name.</summary>
    /// <param name="uniqueName">The unique machine-readable name of the tool.</param>
    public ToolContractAttribute(string uniqueName) : base(uniqueName)
    {
    }
}
