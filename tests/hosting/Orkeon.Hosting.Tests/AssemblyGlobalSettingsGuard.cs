using System.Runtime.CompilerServices;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Assembly-wide guard against machine-environment leakage: a REAL
/// <c>~/.config/Orkeon/appsettings.json</c> (exactly what <c>orkeon init</c> creates) would
/// otherwise be picked up by step 4 of <see cref="RunnerSettings.ResolveSettingsPath"/> in
/// any test exercising the resolution chain without pinning the seam. The module
/// initializer pins the global-settings step onto a guaranteed-nonexistent path before any
/// test runs. Tests that need their own override set it on top and must restore
/// <see cref="DefaultOverride"/> (not <see langword="null"/>) when done.
/// </summary>
internal static class AssemblyGlobalSettingsGuard
{
    /// <summary>A path whose directory is intentionally never created.</summary>
    internal static string DefaultOverride { get; } = Path.Combine(
        Path.GetTempPath(),
        "orkeon-hosting-tests-no-global-" + Guid.NewGuid().ToString("N"),
        "appsettings.json");

    [ModuleInitializer]
    internal static void Initialize() =>
        RunnerSettings.GlobalSettingsPathOverride = DefaultOverride;
}
