using System.Text.RegularExpressions;

namespace Orkeon.Scripting.Versioning;

/// <summary>
/// Parses the <c>/// &lt;reference orkeon-script="X.Y" /&gt;</c> directive that may appear
/// at the head of a <c>.ork.ts</c> source. The directive is a plain TypeScript comment
/// and is consumed by the runtime, not the transpiler.
/// </summary>
public static partial class VersionDirectiveParser
{
    /// <summary>Default version assumed when a script omits the directive.</summary>
    public const string DefaultVersion = "1.0";

    /// <summary>Versions this build of the runtime understands.</summary>
    public static readonly IReadOnlySet<string> SupportedVersions = new HashSet<string>(StringComparer.Ordinal)
    {
        "1.0",
    };

    [GeneratedRegex(
        """^\s*///\s*<reference\s+orkeon-script\s*=\s*"(?<version>[^"]+)"\s*/?>""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex DirectivePattern();

    /// <summary>
    /// Returns the declared version if present in the first lines of <paramref name="source"/>,
    /// or <see cref="DefaultVersion"/> when the directive is omitted.
    /// </summary>
    public static string ParseOrDefault(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var match = DirectivePattern().Match(source);
        return match.Success ? match.Groups["version"].Value : DefaultVersion;
    }

    /// <summary>
    /// Parses the version directive and validates it against <see cref="SupportedVersions"/>.
    /// Throws <see cref="ScriptVersionMismatchError"/> when the declared version is not supported.
    /// </summary>
    public static string ParseAndValidate(string source)
    {
        var declared = ParseOrDefault(source);
        if (!SupportedVersions.Contains(declared))
            throw new ScriptVersionMismatchError(declared, string.Join(", ", SupportedVersions));
        return declared;
    }
}
