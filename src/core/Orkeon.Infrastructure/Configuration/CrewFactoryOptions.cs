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
}
