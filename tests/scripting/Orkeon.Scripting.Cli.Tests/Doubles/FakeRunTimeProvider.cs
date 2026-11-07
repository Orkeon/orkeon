namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>A clock the test scripts: each read advances by one second, so the lines of a stream differ visibly and deterministically.</summary>
internal sealed class FakeRunTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Reads the current instant and advances by one second.</summary>
    public override DateTimeOffset GetUtcNow()
    {
        var now = _now;
        _now = _now.AddSeconds(1);
        return now;
    }
}
