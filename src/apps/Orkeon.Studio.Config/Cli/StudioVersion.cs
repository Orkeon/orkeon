using System.Globalization;
using System.Reflection;

namespace Orkeon.Studio.Config.Cli;

/// <summary>
/// The version this build reports. It comes from the assembly attributes, which
/// <c>src/Directory.Build.props</c> fills in — the repository's single source of truth —
/// so the launcher can never drift from the packages it ships in.
/// </summary>
internal static class StudioVersion
{
    /// <summary>Product name printed next to the version.</summary>
    public const string ProductName = "orkeon-studio-config";

    /// <summary>The version alone, e.g. <c>0.9.2-beta</c>.</summary>
    public static string Value { get; } = Resolve();

    /// <summary>The single line <c>--version</c> prints.</summary>
    public static string Line { get; } =
        string.Create(CultureInfo.InvariantCulture, $"{ProductName} {Value}");

    private static string Resolve()
    {
        var assembly = typeof(StudioVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the "+<commit sha>" SourceLink suffix: the smokes compare a plain version.
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
