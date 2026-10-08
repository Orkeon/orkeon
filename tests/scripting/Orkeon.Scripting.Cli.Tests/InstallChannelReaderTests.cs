using Orkeon.Constants.FileSystem;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The channel an installation says it came through (<see cref="InstallChannelReader"/>): the
/// marker each packaging writes, at each place a layout puts it, and what is answered when
/// there is none or when it says nothing usable.
/// </summary>
public sealed class InstallChannelReaderTests : IDisposable
{
    private readonly ScriptScratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    /// <summary>The directory an archive or an MSI puts the executable in, under <paramref name="root"/>.</summary>
    private static string ExecutableDirectory(string root)
    {
        var directory = Path.Combine(root, "libexec", "orkeon");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteMarker(string directory, string content) =>
        File.WriteAllText(Path.Combine(directory, InstallChannels.MarkerFile), content);

    [Theory]
    [InlineData(InstallChannels.Zip)]
    [InlineData(InstallChannels.Msi)]
    [InlineData(InstallChannels.MsiHost)]
    [InlineData(InstallChannels.Tarball)]
    [InlineData(InstallChannels.Deb)]
    [InlineData(InstallChannels.Homebrew)]
    [InlineData(InstallChannels.Source)]
    public void AMarkerAtTheRootOfTheInstallation_NamesItsChannel(string channel)
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, channel + "\n");

        var reading = InstallChannelReader.Read(executable);

        Assert.Equal(channel, reading.Channel);
        Assert.Null(reading.Problem);
    }

    /// <summary>
    /// An MSI is harvested from the extracted zip, marker included: what tells the two apart is
    /// the marker the MSI build writes in its place, and only that.
    /// </summary>
    [Fact]
    public void TheSameTree_IsTheChannelItsMarkerNames_NotTheOneItsShapeSuggests()
    {
        var executable = ExecutableDirectory(_scratch.Root);
        File.WriteAllText(Path.Combine(_scratch.Root, "VERSION"), "1.0.0-rc.4\n");

        WriteMarker(_scratch.Root, InstallChannels.Zip);
        Assert.Equal(InstallChannels.Zip, InstallChannelReader.Read(executable).Channel);

        WriteMarker(_scratch.Root, InstallChannels.Msi);
        Assert.Equal(InstallChannels.Msi, InstallChannelReader.Read(executable).Channel);
    }

    [Fact]
    public void AMarkerBesideTheExecutable_IsRead_TheDebianLayout()
    {
        WriteMarker(_scratch.Root, InstallChannels.Deb + "\n");

        Assert.Equal(InstallChannels.Deb, InstallChannelReader.Read(_scratch.Root).Channel);
    }

    [Fact]
    public void AMarkerOneLevelUp_IsRead_TheHomebrewLayout()
    {
        var libexec = Path.Combine(_scratch.Root, "libexec");
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(libexec, InstallChannels.Homebrew + "\n");

        Assert.Equal(InstallChannels.Homebrew, InstallChannelReader.Read(executable).Channel);
    }

    [Fact]
    public void TheNearestMarkerWins()
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, InstallChannels.Zip);
        WriteMarker(executable, InstallChannels.Deb);

        Assert.Equal(InstallChannels.Deb, InstallChannelReader.Read(executable).Channel);
    }

    [Fact]
    public void AMarkerAboveTheRootOfTheInstallation_IsNotThisInstallations()
    {
        var root = Path.Combine(_scratch.Root, "Programs", "Orkeon");
        var executable = ExecutableDirectory(root);
        WriteMarker(Path.Combine(_scratch.Root, "Programs"), InstallChannels.Zip);

        var reading = InstallChannelReader.Read(executable);

        Assert.Equal(InstallChannels.Unknown, reading.Channel);
        Assert.Null(reading.Problem);
    }

    [Fact]
    public void ATrailingDirectorySeparator_ChangesNothing()
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, InstallChannels.Tarball);

        var reading = InstallChannelReader.Read(executable + Path.DirectorySeparatorChar);

        Assert.Equal(InstallChannels.Tarball, reading.Channel);
    }

    [Theory]
    [InlineData("zip\r\n")]
    [InlineData("  zip  ")]
    [InlineData("zip\nwhatever a later version adds below\n")]
    public void OnlyTheFirstLineCounts_AndItsSurroundingBlanksDoNot(string content)
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, content);

        Assert.Equal(InstallChannels.Zip, InstallChannelReader.Read(executable).Channel);
    }

    /// <summary>An installation older than the marker, or a build tree: unknown, and not a fault.</summary>
    [Fact]
    public void NoMarker_IsUnknown_WithoutAProblem()
    {
        var reading = InstallChannelReader.Read(ExecutableDirectory(_scratch.Root));

        Assert.Equal(InstallChannels.Unknown, reading.Channel);
        Assert.Null(reading.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   \n")]
    public void AnEmptyMarker_IsUnknown_AndSaysSo(string content)
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, content);

        var reading = InstallChannelReader.Read(executable);

        Assert.Equal(InstallChannels.Unknown, reading.Channel);
        Assert.Contains("is empty", reading.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("snap")]
    [InlineData("ZIP")]
    [InlineData("dotnet-tool")]
    [InlineData("unknown")]
    public void AValueNoPackagingWrites_IsUnknown_AndIsNamed(string value)
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, value + "\n");

        var reading = InstallChannelReader.Read(executable);

        Assert.Equal(InstallChannels.Unknown, reading.Channel);
        Assert.Contains($"'{value}'", reading.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkerThatIsNoText_IsUnknown_AndItsBytesAreNotPrintedRaw()
    {
        var executable = ExecutableDirectory(_scratch.Root);
        File.WriteAllBytes(
            Path.Combine(_scratch.Root, InstallChannels.MarkerFile),
            [.. Enumerable.Repeat((byte)0x07, 4096)]);

        var reading = InstallChannelReader.Read(executable);

        Assert.Equal(InstallChannels.Unknown, reading.Channel);
        Assert.NotNull(reading.Problem);
        Assert.DoesNotContain('\a', reading.Problem);
        Assert.True(reading.Problem.Length < 200, reading.Problem);
    }

    [Fact]
    public void AMarkerThatCannotBeOpened_IsUnknown_AndSaysSo()
    {
        var executable = ExecutableDirectory(_scratch.Root);
        WriteMarker(_scratch.Root, InstallChannels.Zip);
        // Held without sharing: the reader's own open is refused, as a locked or unreadable file is.
        using var held = new FileStream(
            Path.Combine(_scratch.Root, InstallChannels.MarkerFile), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var reading = InstallChannelReader.Read(executable);

        Assert.Equal(InstallChannels.Unknown, reading.Channel);
        Assert.Contains("cannot be read", reading.Problem, StringComparison.Ordinal);
    }

    /// <summary>The dotnet tool has no marker: NuGet unpacks the package and nothing else.</summary>
    [Theory]
    [InlineData(".dotnet/tools/.store/orkeon.scripting.cli/1.0.0-rc.4/orkeon.scripting.cli/1.0.0-rc.4/tools/net10.0/any")]
    [InlineData(".nuget/packages/orkeon.scripting.cli/1.0.0-rc.4/tools/net10.0/any")]
    [InlineData(".nuget/packages/Orkeon.Scripting.Cli/1.0.0/tools/net10.0/any")]
    public void WithoutAMarker_TheToolIsKnownByItsPath(string relative)
    {
        var directory = Path.Combine(_scratch.Root, Path.Combine(relative.Split('/')));
        Directory.CreateDirectory(directory);

        var reading = InstallChannelReader.Read(directory);

        Assert.Equal(InstallChannels.DotnetTool, reading.Channel);
        Assert.Null(reading.Problem);
    }

    [Theory]
    [InlineData("src/scripting/Orkeon.Scripting.Cli/bin/Debug/net10.0")]
    [InlineData("tools/orkeon/bin")]
    [InlineData("orkeon.scripting.cli/tools")]
    public void APathThatOnlyLooksLikeIt_IsNotTheTool(string relative)
    {
        var directory = Path.Combine(_scratch.Root, Path.Combine(relative.Split('/')));
        Directory.CreateDirectory(directory);

        Assert.Equal(InstallChannels.Unknown, InstallChannelReader.Read(directory).Channel);
    }

    [Theory]
    [InlineData(InstallChannels.Zip, "install.cmd")]
    [InlineData(InstallChannels.Msi, "orkeon-<version>-win-x64.msi")]
    [InlineData(InstallChannels.MsiHost, "orkeon-host-<version>-win-x64.msi")]
    [InlineData(InstallChannels.Tarball, "install.sh")]
    [InlineData(InstallChannels.Deb, "sudo apt update && sudo apt upgrade")]
    [InlineData(InstallChannels.Deb, "docs/guides/install-with-apt.md")]
    [InlineData(InstallChannels.Homebrew, "brew upgrade orkeon")]
    [InlineData(InstallChannels.Source, "git pull")]
    [InlineData(InstallChannels.DotnetTool, "dotnet tool update -g Orkeon.Scripting.Cli")]
    [InlineData(InstallChannels.Unknown, "three-ways-to-run-orkeon.md#update-orkeon")]
    public void EveryChannel_HasTheCommandThatUpdatesIt(string channel, string expected)
    {
        Assert.Contains(expected, InstallChannelReader.UpdateHint(channel), StringComparison.Ordinal);
    }

    [Fact]
    public void TheVerboseVersion_KeepsTheVersionLine_AndAddsOneChannelLine()
    {
        var text = CliUsage.RenderVerboseVersion(new InstallChannelReading(InstallChannels.Msi));

        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal([CliUsage.VersionLine, "channel: msi"], lines);
    }
}
