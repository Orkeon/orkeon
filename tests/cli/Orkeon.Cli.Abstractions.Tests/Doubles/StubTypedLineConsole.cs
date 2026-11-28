using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>
/// A <see cref="StubLineConsole"/> that states where its lines came from: typed at an interactive terminal
/// (<see cref="Typed"/> true, the default) or not (a replay file).
/// </summary>
public sealed class StubTypedLineConsole(params string[] lines) : StubLineConsole(lines), IReplLineOrigin
{
    /// <summary>Whether the lines served count as typed by the operator.</summary>
    public bool Typed { get; set; } = true;

    /// <inheritdoc />
    public bool LastLineTyped => Typed;
}
