namespace Orkeon.Hosting.Tests;

/// <summary>
/// Contract tests for <see cref="RunnerSettings.ResolveSettingsPath"/> — the
/// appsettings fallback chain shared by all runners. Tests use real temp
/// directories (allowed for tests per the VFS policy).
/// </summary>
public sealed class RunnerSettingsContractTests : IDisposable
{
    private readonly string _root;

    public RunnerSettingsContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void Explicit_path_wins_when_it_exists()
    {
        var explicitSettings = Path.Combine(_root, "custom.json");
        File.WriteAllText(explicitSettings, "{}");

        var resolved = RunnerSettings.ResolveSettingsPath(explicitSettings, _root);

        Assert.Equal(Path.GetFullPath(explicitSettings), resolved);
    }

    [Fact]
    public void Missing_explicit_path_returns_null_without_fallback()
    {
        // Even though a local appsettings.json exists, a broken explicit path
        // must NOT silently fall back to it — the user asked for a specific file.
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{}");

        var resolved = RunnerSettings.ResolveSettingsPath(
            Path.Combine(_root, "does-not-exist.json"), _root);

        Assert.Null(resolved);
    }

    [Fact]
    public void Local_appsettings_next_to_config_is_used()
    {
        var local = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(local, "{}");

        var resolved = RunnerSettings.ResolveSettingsPath(null, _root);

        Assert.Equal(local, resolved);
    }

    [Fact]
    public void Walks_up_to_canonical_appsettings()
    {
        var settingsDir = Path.Combine(_root, "appsettings");
        Directory.CreateDirectory(settingsDir);
        var canonical = Path.Combine(settingsDir, "appsettings.json");
        File.WriteAllText(canonical, "{}");

        var configDir = Path.Combine(_root, "examples", "deep");
        Directory.CreateDirectory(configDir);

        var resolved = RunnerSettings.ResolveSettingsPath(null, configDir);

        Assert.Equal(canonical, resolved);
    }

    [Fact]
    public void Walks_up_to_legacy_shared_appsettings_when_canonical_absent()
    {
        // Deprecated compatibility fallback: the _shared location is still honored
        // when no appsettings/appsettings.json exists above the config dir.
        var sharedDir = Path.Combine(_root, "_shared");
        Directory.CreateDirectory(sharedDir);
        var shared = Path.Combine(sharedDir, "appsettings.json");
        File.WriteAllText(shared, "{}");

        var configDir = Path.Combine(_root, "examples", "deep");
        Directory.CreateDirectory(configDir);

        var resolved = RunnerSettings.ResolveSettingsPath(null, configDir);

        Assert.Equal(shared, resolved);
    }

    [Fact]
    public void Canonical_appsettings_wins_over_legacy_shared()
    {
        // When both live at the same level, the canonical appsettings/ folder
        // takes precedence over the deprecated _shared/ fallback.
        var settingsDir = Path.Combine(_root, "appsettings");
        Directory.CreateDirectory(settingsDir);
        var canonical = Path.Combine(settingsDir, "appsettings.json");
        File.WriteAllText(canonical, "{}");

        var sharedDir = Path.Combine(_root, "_shared");
        Directory.CreateDirectory(sharedDir);
        File.WriteAllText(Path.Combine(sharedDir, "appsettings.json"), "{}");

        var configDir = Path.Combine(_root, "examples", "deep");
        Directory.CreateDirectory(configDir);

        var resolved = RunnerSettings.ResolveSettingsPath(null, configDir);

        Assert.Equal(canonical, resolved);
    }

    [Fact]
    public void Walk_up_stops_at_examples_solution_marker()
    {
        // _shared/appsettings.json above the marker must NOT be picked up.
        var sharedDir = Path.Combine(_root, "_shared");
        Directory.CreateDirectory(sharedDir);
        File.WriteAllText(Path.Combine(sharedDir, "appsettings.json"), "{}");

        var slnDir = Path.Combine(_root, "examples");
        Directory.CreateDirectory(slnDir);
        File.WriteAllText(Path.Combine(slnDir, "Orkeon.Examples.sln"), "");

        var configDir = Path.Combine(slnDir, "category", "demo");
        Directory.CreateDirectory(configDir);

        var resolved = RunnerSettings.ResolveSettingsPath(null, configDir);

        Assert.Null(resolved);
    }
}
