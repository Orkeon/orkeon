namespace Orkeon.Tests.Shared.Constants;

/// <summary>Durées et délais de test.</summary>
public static class TestTimingConstants
{
    public static readonly TimeSpan TimeoutStandard = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TimeoutExtended = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TimeoutLong = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TimeoutQuick = TimeSpan.FromSeconds(30);
}
