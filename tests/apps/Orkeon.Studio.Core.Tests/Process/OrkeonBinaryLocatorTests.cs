using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// Resolution of the co-installed <c>orkeon</c> binary. Every case runs against
/// <see cref="FakeExecutableProbe"/>: the suite must pass on a machine where the CLI is
/// installed and on one where it is not.
/// </summary>
public sealed class OrkeonBinaryLocatorTests
{
    private static readonly string[] UnixNames = ["orkeon"];

    [Fact]
    public void The_binary_next_to_studio_is_found_first()
    {
        var installDir = Path.Combine("/", "opt", "orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon"))
            .WithPathDirectories(Path.Combine("/", "usr", "bin"))
            .WithFile(Path.Combine("/", "usr", "bin", "orkeon"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.True(location.Found);
        Assert.Equal(Path.Combine(installDir, "orkeon"), location.Path);
        Assert.Equal(BinarySource.InstallDirectory, location.Source);
        Assert.Null(location.Error);
    }

    [Fact]
    public void The_bin_sibling_of_the_install_directory_is_probed()
    {
        // Archive layout: the apps sit in libexec/<app>/ and every launcher in bin/.
        var root = Path.Combine("/", "opt", "orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = Path.Combine(root, "libexec") }
            .WithFile(Path.Combine(root, "bin", "orkeon"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.True(location.Found);
        Assert.Equal(Path.Combine(root, "bin", "orkeon"), location.Path);
        Assert.Equal(BinarySource.InstallDirectory, location.Source);
    }

    [Fact]
    public void The_search_path_is_the_fallback()
    {
        var probe = new FakeExecutableProbe { BaseDirectory = Path.Combine("/", "opt", "studio") }
            .WithPathDirectories(Path.Combine("/", "nowhere"), Path.Combine("/", "usr", "local", "bin"))
            .WithFile(Path.Combine("/", "usr", "local", "bin", "orkeon"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.True(location.Found);
        Assert.Equal(Path.Combine("/", "usr", "local", "bin", "orkeon"), location.Path);
        Assert.Equal(BinarySource.SearchPath, location.Source);
    }

    [Fact]
    public void A_missing_binary_is_an_actionable_result_not_an_exception()
    {
        var probe = new FakeExecutableProbe { BaseDirectory = Path.Combine("/", "opt", "studio") }
            .WithPathDirectories(Path.Combine("/", "usr", "bin"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.False(location.Found);
        Assert.Null(location.Path);
        Assert.Equal(BinarySource.NotFound, location.Source);
        Assert.NotNull(location.Error);

        // The message must name the missing tool, say what breaks, and say what to do.
        Assert.Contains("orkeon", location.Error, StringComparison.Ordinal);
        Assert.Contains("not found", location.Error, StringComparison.Ordinal);
        Assert.Contains("reinstall", location.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PATH", location.Error, StringComparison.Ordinal);
        Assert.Contains(probe.BaseDirectory, location.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_probed_path_is_reported_so_the_ui_can_show_where_it_looked()
    {
        var probe = new FakeExecutableProbe { BaseDirectory = Path.Combine("/", "opt", "studio") }
            .WithPathDirectories(Path.Combine("/", "usr", "bin"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.Contains(Path.Combine("/", "opt", "studio", "orkeon"), location.ProbedPaths);
        Assert.Contains(Path.Combine("/", "usr", "bin", "orkeon"), location.ProbedPaths);
        Assert.Equal(probe.ProbedPaths, location.ProbedPaths);
    }

    [Fact]
    public void The_windows_executable_name_is_tried_before_the_bare_one()
    {
        var installDir = Path.Combine("C:", "Program Files", "Orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon.exe"));

        var location = new OrkeonBinaryLocator(probe, ["orkeon.exe", "orkeon"]).Locate();

        Assert.True(location.Found);
        Assert.Equal(Path.Combine(installDir, "orkeon.exe"), location.Path);
    }

    [Fact]
    public void The_default_executable_names_match_the_platform()
    {
        var names = OrkeonBinaryLocator.DefaultFileNames();

        if (OperatingSystem.IsWindows())
            Assert.Equal(["orkeon.exe", "orkeon"], names);
        else
            Assert.Equal(["orkeon"], names);
    }

    [Fact]
    public void An_empty_base_directory_falls_through_to_the_search_path()
    {
        var probe = new FakeExecutableProbe { BaseDirectory = "" }
            .WithPathDirectories(Path.Combine("/", "usr", "bin"))
            .WithFile(Path.Combine("/", "usr", "bin", "orkeon"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.True(location.Found);
        Assert.Equal(BinarySource.SearchPath, location.Source);
    }
}
