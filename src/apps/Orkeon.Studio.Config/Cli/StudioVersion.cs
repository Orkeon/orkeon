using Orkeon.Studio.Core;

namespace Orkeon.Studio.Config.Cli;

/// <summary>
/// The version this build reports. It comes from the assembly attributes, which
/// <c>src/Directory.Build.props</c> fills in — the repository's single source of truth —
/// so the editor can never drift from the packages it ships in. The reading itself is
/// <see cref="StudioAssemblyInfo"/>'s, shared with the launcher.
/// </summary>
internal static class StudioVersion
{
    /// <summary>Product name printed next to the version.</summary>
    public const string ProductName = "orkeon-studio-config";

    /// <summary>The version alone, e.g. <c>0.9.2-beta</c>.</summary>
    public static string Value { get; } = StudioAssemblyInfo.VersionOf(typeof(StudioVersion).Assembly);

    /// <summary>The single line <c>--version</c> prints.</summary>
    public static string Line { get; } =
        StudioAssemblyInfo.VersionLine(ProductName, typeof(StudioVersion).Assembly);
}
