namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Options controlling how <see cref="CrewFactory"/> materialises a crew from configuration.
/// Runners read the <c>Orkeon:CrewFactory:StrictTools</c> configuration key (defaulting it to
/// <see langword="true"/>); the library default is lenient.
/// </summary>
public sealed class CrewFactoryOptions
{
    /// <summary>
    /// When <c>true</c>, a crew definition that references a tool absent from the
    /// <see cref="Orkeon.Domain.Tools.IToolRegistry"/> fails loading with an explicit
    /// <see cref="System.InvalidOperationException"/> listing the missing and available tools.
    /// When <c>false</c> (the library default), missing tools are logged as warnings and
    /// skipped so the crew loads with whatever tools resolved.
    /// Runners enable strict mode so a typo in a crew's tool list surfaces immediately
    /// instead of degrading the agent silently.
    /// </summary>
    public bool StrictTools { get; set; }

    /// <summary>
    /// When <c>true</c> (the default), a crew configuration carrying a <c>rag:</c> block
    /// with declared collections has them ingested at crew creation through the optional
    /// <see cref="Orkeon.Rag.Abstractions.Interfaces.IRagCollectionsBootstrapper"/>
    /// (incremental — a fresh manifest is a no-op). Validation-only hosts
    /// (e.g. <c>--validate</c>) disable it so loading a crew never touches the index.
    /// </summary>
    public bool PrepareRagCollections { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (the default), loading a crew logs a warning for each task whose
    /// <c>tool_call</c> deliverable no agent that may run it can write
    /// (<see cref="Orkeon.Application.Crew.DeliverableToolCheck"/>, #392). <c>orkeon run --validate</c>
    /// turns it off: it prints the same findings as its own <c>WARNING:</c> lines, once each.
    /// </summary>
    public bool WarnOnDeliverableTools { get; set; } = true;
}
