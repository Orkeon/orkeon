namespace Orkeon.Hosting.Tests;

/// <summary>
/// Contract tests for <see cref="RunnerSettings.ResolveSettingsPath"/> — the
/// appsettings fallback chain shared by all runners. Tests use real temp
/// directories (allowed for tests per the VFS policy). Runs in the serial
/// console collection: two tests capture the process-global stderr, and the
/// global-path override seam is process-global static state.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerSettingsContractTests : IDisposable
{
    private readonly string _root;
    private readonly string _globalSettings;

    public RunnerSettingsContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        // Pin the global per-user step (WIN-02) onto a per-test path so a real
        // %APPDATA%/Orkeon/appsettings.json on the machine can never leak into a test.
        _globalSettings = Path.Combine(_root, "global", "Orkeon", "appsettings.json");
        RunnerSettings.GlobalSettingsPathOverride = _globalSettings;
    }

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
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
    public void Global_user_settings_are_used_when_nothing_local_matches()
    {
        // WIN-02 step 4: the config written by `orkeon init` at the global per-user
        // path must be found from ANY working directory.
        Directory.CreateDirectory(Path.GetDirectoryName(_globalSettings)!);
        File.WriteAllText(_globalSettings, "{}");

        var configDir = Path.Combine(_root, "anywhere", "deep");
        Directory.CreateDirectory(configDir);

        var resolved = RunnerSettings.ResolveSettingsPath(null, configDir);

        Assert.Equal(_globalSettings, resolved);
    }

    [Fact]
    public void Local_appsettings_wins_over_global_user_settings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_globalSettings)!);
        File.WriteAllText(_globalSettings, "{}");

        var local = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(local, "{}");

        var resolved = RunnerSettings.ResolveSettingsPath(null, _root);

        Assert.Equal(local, resolved);
    }

    [Fact]
    public void No_settings_warning_mentions_orkeon_init()
    {
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        string? resolved;
        try
        {
            resolved = RunnerSettings.ResolveSettingsPath(null, _root);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Null(resolved);
        Assert.Contains("orkeon init", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Quiet_resolution_writes_nothing_to_stderr()
    {
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        string? resolved;
        try
        {
            resolved = RunnerSettings.ResolveSettingsPath(null, _root, quiet: true);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Null(resolved);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Global_settings_path_is_always_absolute_even_without_config_env_vars()
    {
        // Nominal-path guard (WIN-02 .deb smoke): with XDG_CONFIG_HOME/APPDATA unset, the
        // real GetFolderPath(ApplicationData, Create) must still yield an ABSOLUTE path
        // (on Unix it resolves the home via getpwuid, so it does not return "" here — the
        // empty-return branches are covered by the SpecialFolderPathOverride seam tests
        // below, which simulate the bare-container case).
        RunnerSettings.GlobalSettingsPathOverride = null; // exercise the real computation
        var oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var oldAppData = Environment.GetEnvironmentVariable("APPDATA");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            Environment.SetEnvironmentVariable("APPDATA", null);

            var path = RunnerSettings.GetGlobalSettingsPath();

            Assert.True(Path.IsPathRooted(path), $"expected an absolute path, got '{path}'");
            Assert.EndsWith(
                Path.Combine("Orkeon", "appsettings.json"), path, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            Environment.SetEnvironmentVariable("APPDATA", oldAppData);
            RunnerSettings.GlobalSettingsPathOverride = _globalSettings;
        }
    }

    [Fact]
    public void Global_path_falls_back_to_home_config_when_special_folders_are_empty()
    {
        // Bare-container branch (WIN-02 .deb smoke): GetFolderPath returns "" for both
        // ApplicationData and UserProfile → the path is derived from HOME and stays absolute.
        RunnerSettings.GlobalSettingsPathOverride = null;
        RunnerSettings.SpecialFolderPathOverride = _ => "";
        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            var home = Path.Combine(_root, "fresh-home");
            Environment.SetEnvironmentVariable("HOME", home);

            var path = RunnerSettings.GetGlobalSettingsPath();

            Assert.True(Path.IsPathRooted(path), $"expected an absolute path, got '{path}'");
            var expected = OperatingSystem.IsWindows()
                ? Path.Combine(home, "AppData", "Roaming", "Orkeon", "appsettings.json")
                : Path.Combine(home, ".config", "Orkeon", "appsettings.json");
            Assert.Equal(expected, path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", oldHome);
            RunnerSettings.SpecialFolderPathOverride = null;
            RunnerSettings.GlobalSettingsPathOverride = _globalSettings;
        }
    }

    [Fact]
    public void Global_path_fails_actionably_when_no_home_is_resolvable()
    {
        // Worst case: no ApplicationData, no UserProfile, no HOME. The failure must be a
        // typed exception with an actionable message, never a silently relative path.
        RunnerSettings.GlobalSettingsPathOverride = null;
        RunnerSettings.SpecialFolderPathOverride = _ => "";
        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", null);

            var ex = Assert.Throws<InvalidOperationException>(RunnerSettings.GetGlobalSettingsPath);

            Assert.Contains("HOME", ex.Message, StringComparison.Ordinal);
            Assert.Contains("--path", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", oldHome);
            RunnerSettings.SpecialFolderPathOverride = null;
            RunnerSettings.GlobalSettingsPathOverride = _globalSettings;
        }
    }

    [Fact]
    public void Resolution_degrades_to_env_vars_only_when_no_home_is_resolvable()
    {
        // ResolveSettingsPath must not crash a runner on a bare container: step 4 simply
        // finds nothing and the chain falls through to env-vars-only.
        RunnerSettings.GlobalSettingsPathOverride = null;
        RunnerSettings.SpecialFolderPathOverride = _ => "";
        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", null);

            var resolved = RunnerSettings.ResolveSettingsPath(null, _root, quiet: true);

            Assert.Null(resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", oldHome);
            RunnerSettings.SpecialFolderPathOverride = null;
            RunnerSettings.GlobalSettingsPathOverride = _globalSettings;
        }
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
