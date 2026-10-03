using Orkeon.Host;

namespace Orkeon.Host.Tests;

/// <summary>
/// Where the daemon reads the configuration that decides its VFS mounts.
/// <para>
/// A bare <see cref="Microsoft.Extensions.Configuration.ConfigurationBuilder"/> resolves a
/// relative file against <see cref="AppContext.BaseDirectory"/> — the executable's own folder
/// — while <c>orkeon-host --help</c> promises <c>./appsettings.json</c>, the settings probe
/// checks the working directory, and the host built moments later reads the working directory
/// too. The daemon therefore took its crew list from one file and everything else from
/// another, silently, and an operator running it from their crews folder got a daemon hosting
/// nothing.
/// </para>
/// </summary>
[Collection(nameof(WorkingDirectoryCollection))]
public sealed class BootConfigurationTests : IDisposable
{
    private readonly string _scratch;
    private readonly string _originalWorkingDirectory;

    public BootConfigurationTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"orkeon-host-boot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
        _originalWorkingDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_scratch);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalWorkingDirectory);
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public void ABareAppsettingsIsReadFromTheWorkingDirectory()
    {
        File.WriteAllText(
            Path.Combine(_scratch, "appsettings.json"),
            """{ "Orkeon": { "Host": { "RunTimeout": "00:07:00" } } }""");

        var configuration = StartupProbes.BuildBootConfiguration(settingsPath: null);

        Assert.Equal("00:07:00", configuration["Orkeon:Host:RunTimeout"]);
    }

    [Fact]
    public void ARelativeSettingsPathIsReadFromTheWorkingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "conf"));
        File.WriteAllText(
            Path.Combine(_scratch, "conf", "host.json"),
            """{ "Orkeon": { "Host": { "RunTimeout": "00:09:00" } } }""");

        var configuration = StartupProbes.BuildBootConfiguration(Path.Combine("conf", "host.json"));

        Assert.Equal("00:09:00", configuration["Orkeon:Host:RunTimeout"]);
    }

    [Fact]
    public void AnAbsoluteSettingsPathStillWins()
    {
        var elsewhere = Path.Combine(_scratch, "elsewhere.json");
        File.WriteAllText(elsewhere, """{ "Orkeon": { "Host": { "RunTimeout": "00:11:00" } } }""");

        var configuration = StartupProbes.BuildBootConfiguration(elsewhere);

        Assert.Equal("00:11:00", configuration["Orkeon:Host:RunTimeout"]);
    }

    /// <summary>
    /// GAP-36, decision 5: <c>./appsettings.json</c> is the daemon's settings file when
    /// <c>--settings</c> names none — never a layer under the file it names, which would receive,
    /// unseen, the crews, profiles and mounts of the other one.
    /// </summary>
    [Fact]
    public void ANamedSettingsFileReplacesTheWorkingDirectorysAppsettings_NeverLaysOverIt()
    {
        File.WriteAllText(
            Path.Combine(_scratch, "appsettings.json"),
            """{ "Orkeon": { "Host": { "ShutdownGracePeriod": "00:00:42" } } }""");
        var named = Path.Combine(_scratch, "host.json");
        File.WriteAllText(named, """{ "Orkeon": { "Host": { "RunTimeout": "00:09:00" } } }""");

        var configuration = StartupProbes.BuildBootConfiguration(named);

        Assert.Equal("00:09:00", configuration["Orkeon:Host:RunTimeout"]);
        Assert.Null(configuration["Orkeon:Host:ShutdownGracePeriod"]);
    }
}

/// <summary>
/// Serialises the tests that move the process working directory — it is process-wide state,
/// and xUnit runs collections in parallel by default.
/// </summary>
[CollectionDefinition(nameof(WorkingDirectoryCollection), DisableParallelization = true)]
public sealed class WorkingDirectoryCollection;
