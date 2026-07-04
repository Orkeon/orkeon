using Orkeon.Application.Interfaces.Compliance;

namespace Orkeon.Application.Tests.Compliance;

public class NistControlMappingTestsFixture
{
    /// <summary>
    /// Returns all known NIST family codes in the catalog.
    /// </summary>
    public static IReadOnlyList<string> AllExpectedFamilies { get; } =
    [
        "AC", "AU", "CM", "IR", "RA", "SI", "SC", "PM"
    ];

    /// <summary>
    /// Returns all controls from the catalog.
    /// </summary>
    public IReadOnlyList<NistControl> AllControls { get; } = NistControlMapping.GetAllControls();
}
