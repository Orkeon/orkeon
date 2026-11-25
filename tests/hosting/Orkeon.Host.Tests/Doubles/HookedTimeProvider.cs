namespace Orkeon.Host.Tests.Doubles;

/// <summary>The system's clock, with what a test does each time it is read.</summary>
internal sealed class HookedTimeProvider : TimeProvider
{
    public Action? OnRead;

    public override DateTimeOffset GetUtcNow()
    {
        OnRead?.Invoke();
        return base.GetUtcNow();
    }
}
