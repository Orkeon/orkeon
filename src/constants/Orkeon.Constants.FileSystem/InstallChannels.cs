namespace Orkeon.Constants.FileSystem;

/// <summary>
/// The channels Orkeon is installed through, and the marker file that says which one put an
/// installation where it is.
/// <para>
/// The marker is written by whoever packs or installs — a packaging script, an MSI build, the
/// Debian package, the Homebrew formula — and read by <c>orkeon doctor</c> and
/// <c>orkeon --version --verbose</c>; Orkeon Studio reads the same names. Nothing deduces a
/// channel from the shape of an installation, with one exception: a dotnet tool carries no
/// marker and is known by the path NuGet gives it.
/// </para>
/// </summary>
public static class InstallChannels
{
    /// <summary>
    /// The marker file: one line, one of the values below. It sits at the root of an
    /// installation, next to <c>VERSION</c> where the installation has one.
    /// </summary>
    public const string MarkerFile = "INSTALL-CHANNEL";

    /// <summary>The Windows zip, installed by its <c>install.ps1</c>.</summary>
    public const string Zip = "zip";

    /// <summary>The per-user Windows MSI of the CLI.</summary>
    public const string Msi = "msi";

    /// <summary>The per-machine Windows MSI of the service host.</summary>
    public const string MsiHost = "msi-host";

    /// <summary>The macOS or Linux tarball, installed by its <c>install.sh</c>.</summary>
    public const string Tarball = "tarball";

    /// <summary>
    /// The Debian package — downloaded, or served by the apt repository: one package, and no
    /// file inside it can tell which of the two brought it.
    /// </summary>
    public const string Deb = "deb";

    /// <summary>The Homebrew formula.</summary>
    public const string Homebrew = "homebrew";

    /// <summary>An archive built from a clone and installed on the machine that built it.</summary>
    public const string Source = "source";

    /// <summary>
    /// The dotnet tool. Never written to a marker: read off the path of the tool store or of
    /// the NuGet package folder.
    /// </summary>
    public const string DotnetTool = "dotnet-tool";

    /// <summary>
    /// No marker, or one that names no channel of this list: an installation older than the
    /// marker, a build tree, a channel a later version added.
    /// </summary>
    public const string Unknown = "unknown";
}
