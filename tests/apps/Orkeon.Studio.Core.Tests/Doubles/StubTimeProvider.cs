namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// A <see cref="TimeProvider"/> frozen at <see cref="Now"/>, so a result stamped with the time
/// it was read can be asserted to the tick.
/// </summary>
public sealed class StubTimeProvider : TimeProvider
{
    /// <summary>The instant every call to <see cref="GetUtcNow"/> answers.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}
