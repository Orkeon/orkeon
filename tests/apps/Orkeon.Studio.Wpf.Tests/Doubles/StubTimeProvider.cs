namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// A <see cref="TimeProvider"/> frozen at <see cref="Now"/>: a run's elapsed time moves when the
/// test moves it, never while the test is reading it.
/// </summary>
public sealed class StubTimeProvider : TimeProvider
{
    /// <summary>The instant every call to <see cref="GetUtcNow"/> answers.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}
