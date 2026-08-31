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

    /// <summary>What <see cref="OrkeonBinaryLocator.DefaultFileNames"/> yields on Windows.</summary>
    private static readonly string[] WindowsNames = ["orkeon.exe"];

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

        // The message must name the missing tool, say what breaks, and say what to do. «Not
        // located on this machine» over «not found»: the reader is being told the search ended,
        // not that some particular path was empty.
        Assert.Contains("orkeon", location.Error, StringComparison.Ordinal);
        Assert.Contains("was not located on this machine", location.Error, StringComparison.Ordinal);
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
            Assert.Equal(["orkeon.exe"], names);
        else
            Assert.Equal(["orkeon"], names);
    }

    /// <summary>
    /// A checkout shared between Windows and WSL leaves the Linux apphost — plain <c>orkeon</c>,
    /// no extension — in the very <c>bin/</c> directory the development-tree lookup walks. Windows
    /// cannot start that file, so claiming it was found turned «the CLI is not installed», a state
    /// Studio knows how to present and how to disable its launch features for, into a Win32
    /// «The specified executable is not a valid application for this OS platform» thrown at the
    /// user mid-launch. It must read as not found.
    /// </summary>
    [Fact]
    public void A_build_for_another_platform_does_not_count_as_finding_the_binary()
    {
        var installDir = Path.Combine("C:", "Program Files", "Orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon"));

        var location = new OrkeonBinaryLocator(probe, WindowsNames).Locate();

        Assert.False(location.Found);
        Assert.Equal(BinarySource.NotFound, location.Source);
        Assert.Null(location.Path);
    }

    [Fact]
    public void The_failure_names_the_build_for_another_platform_it_walked_past()
    {
        var installDir = Path.Combine("C:", "Program Files", "Orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon"));

        var error = new OrkeonBinaryLocator(probe, WindowsNames).Locate().Error;

        // Telling someone «not found» while a file called orkeon sits in the folder they are
        // staring at is the least useful true sentence available.
        Assert.NotNull(error);
        Assert.Contains("was not located on this machine", error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(installDir, "orkeon"), error, StringComparison.Ordinal);
        Assert.Contains("another", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_windows_executable_still_wins_over_a_neighbouring_foreign_build()
    {
        var installDir = Path.Combine("C:", "Program Files", "Orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon"))
            .WithFile(Path.Combine(installDir, "orkeon.exe"));

        var location = new OrkeonBinaryLocator(probe, WindowsNames).Locate();

        Assert.True(location.Found);
        Assert.Equal(Path.Combine(installDir, "orkeon.exe"), location.Path);
        Assert.Null(location.Error);
    }

    /// <summary>The unix lookup accepts the bare name, so it must not diagnose one as foreign.</summary>
    [Fact]
    public void The_unix_lookup_is_not_confused_by_the_extension_less_name()
    {
        var installDir = Path.Combine("/", "opt", "orkeon");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon"));

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.True(location.Found);
        Assert.Null(location.Error);
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
    [Fact]
    public void A_development_checkout_finds_the_cli_in_the_scripting_bin()
    {
        // Studio launched from the IDE: its base dir is deep inside the repo's own bin tree,
        // nothing next to it, nothing on PATH — but the CLI was built in its own project.
        var repo = Path.Combine("/", "home", "me", "orkeon");
        var studioBin = Path.Combine(repo, "src", "apps", "Orkeon.Studio.Wpf", "bin", "Debug", "net10.0-windows");
        var cli = Path.Combine(repo, "src", "scripting", "Orkeon.Scripting.Cli", "bin", "Debug", "net10.0", "orkeon");

        var probe = new FakeExecutableProbe { BaseDirectory = studioBin }
            .WithFile(Path.Combine(repo, "Orkeon.sln"))
            .WithFile(cli);

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.True(location.Found);
        Assert.Equal(cli, location.Path);
        Assert.Equal(BinarySource.DevelopmentTree, location.Source);
    }

    [Fact]
    public void A_debug_studio_falls_back_to_a_release_cli_and_never_escapes_the_checkout()
    {
        var repo = Path.Combine("/", "home", "me", "orkeon");
        var studioBin = Path.Combine(repo, "src", "apps", "Orkeon.Studio.Wpf", "bin", "Debug", "net10.0-windows");
        var releaseCli = Path.Combine(repo, "src", "scripting", "Orkeon.Scripting.Cli", "bin", "Release", "net10.0", "orkeon");

        var probe = new FakeExecutableProbe { BaseDirectory = studioBin }
            .WithFile(Path.Combine(repo, "Orkeon.sln"))
            .WithFile(releaseCli);

        var location = new OrkeonBinaryLocator(probe, UnixNames).Locate();

        Assert.Equal(releaseCli, location.Path);
        Assert.Equal(BinarySource.DevelopmentTree, location.Source);

        // Outside a checkout (no Orkeon.sln anywhere above), the fallback stays silent.
        var installed = new FakeExecutableProbe { BaseDirectory = studioBin }.WithFile(releaseCli);
        Assert.False(new OrkeonBinaryLocator(installed, UnixNames).Locate().Found);
    }

    [Fact]
    public void The_operators_directory_beats_the_binary_next_to_studio()
    {
        var installDir = Path.Combine("/", "opt", "orkeon");
        var chosenDir = Path.Combine("/", "srv", "cli");
        var probe = new FakeExecutableProbe { BaseDirectory = installDir }
            .WithFile(Path.Combine(installDir, "orkeon"))
            .WithFile(Path.Combine(chosenDir, "orkeon"));

        var location = new OrkeonBinaryLocator(probe, UnixNames, explicitDirectory: chosenDir).Locate();

        Assert.Equal(Path.Combine(chosenDir, "orkeon"), location.Path);
        Assert.Equal(BinarySource.ExplicitDirectory, location.Source);
    }

    [Fact]
    public void The_environment_variable_is_read_after_the_install_directory_and_before_path()
    {
        var envDir = Path.Combine("/", "srv", "env-cli");
        var pathDir = Path.Combine("/", "usr", "bin");
        var probe = new FakeExecutableProbe { BaseDirectory = Path.Combine("/", "opt", "orkeon") }
            .WithPathDirectories(pathDir)
            .WithFile(Path.Combine(envDir, "orkeon"))
            .WithFile(Path.Combine(pathDir, "orkeon"));

        var location = new OrkeonBinaryLocator(
            probe, UnixNames,
            environment: name => name == OrkeonBinaryLocator.DirectoryEnvironmentVariable ? envDir : null).Locate();

        Assert.Equal(Path.Combine(envDir, "orkeon"), location.Path);
        Assert.Equal(BinarySource.EnvironmentVariable, location.Source);

        // But the install directory still wins over the variable.
        var installed = new FakeExecutableProbe { BaseDirectory = Path.Combine("/", "opt", "orkeon") }
            .WithFile(Path.Combine("/", "opt", "orkeon", "orkeon"))
            .WithFile(Path.Combine(envDir, "orkeon"));
        var installedLocation = new OrkeonBinaryLocator(
            installed, UnixNames,
            environment: _ => envDir).Locate();
        Assert.Equal(BinarySource.InstallDirectory, installedLocation.Source);
    }

    [Fact]
    public void The_not_found_message_names_every_lookup_in_order()
    {
        var location = new OrkeonBinaryLocator(
            new FakeExecutableProbe { BaseDirectory = "/opt/orkeon" }, UnixNames, environment: _ => null).Locate();

        Assert.Contains("--cli-dir", location.Error, StringComparison.Ordinal);
        Assert.Contains(OrkeonBinaryLocator.DirectoryEnvironmentVariable, location.Error, StringComparison.Ordinal);
        Assert.Contains("PATH", location.Error, StringComparison.Ordinal);
    }

}
