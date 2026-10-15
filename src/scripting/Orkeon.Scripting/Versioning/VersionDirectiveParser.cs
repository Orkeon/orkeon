using System.Text.RegularExpressions;

namespace Orkeon.Scripting.Versioning;

/// <summary>
/// Parses the version directive that may appear at the head of a <c>.ork.ts</c> source, in either
/// form: <c>/// &lt;reference orkeon-script="X.Y" /&gt;</c>, or <c>// @orkeon-script X.Y</c>, a plain
/// line comment <c>tsc</c> ignores — it rejects the reference form (TS1084) and then skips its
/// semantic pass. Both are comments, consumed by the runtime, not the transpiler; when a source
/// carries both, the reference form wins.
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

    [GeneratedRegex(
        """^\s*//\s*@orkeon-script\s+(?<version>\S+)\s*$""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex LineCommentDirectivePattern();

    /// <summary>
    /// Returns the declared version if present in the first lines of <paramref name="source"/>,
    /// or <see cref="DefaultVersion"/> when the directive is omitted.
    /// </summary>
    public static string ParseOrDefault(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var match = DirectivePattern().Match(source);
        if (!match.Success)
            match = LineCommentDirectivePattern().Match(source);
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
