using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The settings files the repository holds, by their path from its root: what an operator is handed
/// to copy — the examples' <c>appsettings*.json</c>, their <c>*.json.example</c> templates, the sample
/// an installation carries —, what a release smoke starts a run on, and what Orkeon Studio's capture
/// campaign writes for its screenshots. Listed from what git tracks, so a clone that keeps other
/// projects under <c>examples/others</c> does not pay for them.
/// </summary>
internal static class RepositorySettingsFiles
{
    /// <summary>Where the settings the capture campaign of Orkeon Studio writes are kept, one file per world.</summary>
    public const string StudioCaptureDirectory = "tests/apps/Orkeon.Studio.Wpf.Tests/Capture/Settings";

    private const string SmokeFixtures = "scripts/smoke-onboarding/fixtures/";

    private static readonly string[] s_folders = ["examples", "scripts", StudioCaptureDirectory];

    /// <summary>Every settings file of the repository.</summary>
    public static IReadOnlyList<string> All() => RepositoryFiles.Under(s_folders, IsSettingsFile);

    /// <summary>Those an operator copies or a smoke runs on: the examples' and the smoke fixtures'.</summary>
    public static IReadOnlyList<string> OfTheExamples() =>
        RepositoryFiles.Under(
            ["examples", SmokeFixtures.TrimEnd('/')],
            path => IsSettingsFile(path) || path.EndsWith(".json.example", StringComparison.Ordinal));

    private static bool IsSettingsFile(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (path.StartsWith(SmokeFixtures, StringComparison.Ordinal))
            return name.EndsWith(".json", StringComparison.Ordinal);

        return name.Contains("appsettings", StringComparison.OrdinalIgnoreCase)
            && (name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".json.example", StringComparison.Ordinal));
    }
}
