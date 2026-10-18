namespace Orkeon.Tools.Code.Tests;

/// <summary>
/// The confinement of a test that is not about confinement: off, so the test runs the same on a
/// host with or without a working bubblewrap and inside or outside a container
/// (<see cref="ShellConfinementMode.Auto"/> would pick by machine).
/// </summary>
internal static class TestConfinement
{
    /// <summary>A fresh <see cref="ShellConfinementMode.None"/> section.</summary>
    public static ShellConfinementOptions Off() => new() { Mode = ShellConfinementMode.None };
}
