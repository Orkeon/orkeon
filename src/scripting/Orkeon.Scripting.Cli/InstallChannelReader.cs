using System.Text;
using Orkeon.Constants.FileSystem;

namespace Orkeon.Scripting.Cli;

/// <summary>What <see cref="InstallChannelReader"/> found.</summary>
/// <param name="Channel">One of the <see cref="InstallChannels"/> values; <see cref="InstallChannels.Unknown"/> when nothing names one.</param>
/// <param name="Problem">
/// Why a marker that exists gives no channel — it cannot be read, is empty, or names a channel
/// this build does not know. <see langword="null"/> when there is no marker at all: an
/// installation older than the marker, or a build tree, is not a fault.
/// </param>
internal sealed record InstallChannelReading(string Channel, string? Problem = null);

/// <summary>
/// Reads the channel this build was installed through: the <see cref="InstallChannels.MarkerFile"/>
/// the packaging wrote, looked for from the directory of the executable up to the root of the
/// installation.
/// <para>
/// Nothing is deduced from the shape of an installation, the registry or the package database:
/// a zip and an archive built from a clone are the same tree, and so are an MSI and the zip it
/// was harvested from. The one exception is the dotnet tool, which NuGet unpacks without any
/// file of ours beside it and which its path gives away.
/// </para>
/// </summary>
internal static class InstallChannelReader
{
    /// <summary>The package id NuGet names the tool's folder after, in the store and in the package cache.</summary>
    private const string ToolPackageId = "Orkeon.Scripting.Cli";

    /// <summary>
    /// How far above the executable the marker may sit: beside it (the Debian package), one
    /// level up (Homebrew's <c>libexec</c>), two (<c>libexec/orkeon</c> under the root of an
    /// archive or an MSI).
    /// </summary>
    private const int LevelsAboveTheExecutable = 2;

    /// <summary>A marker is one short word; whatever is longer is not one.</summary>
    private const int MarkerReadLimit = 256;

    private static readonly string[] MarkerChannels =
    [
        InstallChannels.Zip,
        InstallChannels.Msi,
        InstallChannels.MsiHost,
        InstallChannels.Tarball,
        InstallChannels.Deb,
        InstallChannels.Homebrew,
        InstallChannels.Source,
    ];

    /// <summary>Reads the channel of the build that runs.</summary>
    public static InstallChannelReading Read() => Read(AppContext.BaseDirectory);

    /// <summary>Reads the channel of the build whose executable sits in <paramref name="baseDirectory"/>.</summary>
    public static InstallChannelReading Read(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        for (var level = 0; level <= LevelsAboveTheExecutable && directory is not null; level++)
        {
            var marker = Path.Combine(directory, InstallChannels.MarkerFile);
            // OUT-OF-SCOPE: probing the tool's own install directory for the marker its
            // packaging wrote; it sits outside every mount, and no host exists yet.
            if (File.Exists(marker))
                return ReadMarker(marker);

            directory = Path.GetDirectoryName(directory);
        }

        return IsDotnetTool(baseDirectory)
            ? new InstallChannelReading(InstallChannels.DotnetTool)
            : new InstallChannelReading(InstallChannels.Unknown);
    }

    /// <summary>The one-line way to move an installation of <paramref name="channel"/> to a newer version.</summary>
    public static string UpdateHint(string channel) => channel switch
    {
        InstallChannels.Zip => "extract the new orkeon-cli-<version>-win-x64.zip and run its install.cmd",
        InstallChannels.Msi => "run the new orkeon-<version>-win-x64.msi",
        InstallChannels.MsiHost => "run the new orkeon-host-<version>-win-x64.msi",
        InstallChannels.Tarball => "extract the new tarball and run its install.sh",
        InstallChannels.Deb =>
            "`sudo apt update && sudo apt upgrade` when the apt repository is a source of this machine — it then "
            + "updates with the system; else `sudo apt install ./orkeon_<version>_<arch>.deb` (the package cannot tell "
            + "which of the two brought it: docs/guides/install-with-apt.md)",
        InstallChannels.Homebrew => "brew upgrade orkeon",
        InstallChannels.Source =>
            "git pull in the clone, then run scripts/install-from-source.ps1 (Windows) or scripts/install-from-source.sh again",
        InstallChannels.DotnetTool => "dotnet tool update -g Orkeon.Scripting.Cli (--prerelease for a pre-release)",
        _ => "install the newer version the way this one was installed — "
            + "https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/three-ways-to-run-orkeon.md#update-orkeon",
    };

    private static InstallChannelReading ReadMarker(string marker)
    {
        string value;
        try
        {
            // OUT-OF-SCOPE: same probe as above — the marker of the tool's own installation.
            using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[MarkerReadLimit];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var lineEnd = text.IndexOfAny(['\r', '\n']);
            value = (lineEnd < 0 ? text : text[..lineEnd]).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InstallChannelReading(
                InstallChannels.Unknown, $"{InstallChannels.MarkerFile} cannot be read ({ex.GetType().Name})");
        }

        if (value.Length == 0)
            return new InstallChannelReading(InstallChannels.Unknown, $"{InstallChannels.MarkerFile} is empty");

        if (Array.Find(MarkerChannels, channel => string.Equals(value, channel, StringComparison.Ordinal)) is { } known)
            return new InstallChannelReading(known);

        return new InstallChannelReading(
            InstallChannels.Unknown,
            $"{InstallChannels.MarkerFile} names '{Printable(value)}', a channel this build does not know");
    }

    /// <summary>
    /// Whether <paramref name="baseDirectory"/> is where NuGet unpacks the tool:
    /// <c>…/orkeon.scripting.cli/&lt;version&gt;/tools/&lt;tfm&gt;/&lt;rid&gt;/</c>, in the global tool store as in the
    /// package folder a local tool runs from.
    /// </summary>
    private static bool IsDotnetTool(string baseDirectory)
    {
        var segments = baseDirectory.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i + 2 < segments.Length; i++)
        {
            if (string.Equals(segments[i], ToolPackageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[i + 2], "tools", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The value as a diagnostic can print it: short, and without a control character.</summary>
    private static string Printable(string value)
    {
        var shown = value.Length > 40 ? value[..40] + "…" : value;
        return string.Concat(shown.Select(c => char.IsControl(c) ? '?' : c));
    }
}
