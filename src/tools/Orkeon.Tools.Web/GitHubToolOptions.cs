using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Web;

/// <summary>
/// The `Orkeon:Tools:GitHub` section: where the `github` tool finds its token, read once when the tool
/// is registered. Without a token every call is anonymous, and `create_issue` and `create_pr` are refused
/// by GitHub.
/// </summary>
public sealed class GitHubToolOptions
{
    /// <summary>The configuration section the github tool reads.</summary>
    public const string SectionName = "Orkeon:Tools:GitHub";

    /// <summary>The environment variable read when neither the setting nor <see cref="TokenVariable"/> gives a token.</summary>
    public const string DefaultTokenVariable = "GITHUB_TOKEN";

    /// <summary>The environment variable read last, the second name the GitHub CLI reads.</summary>
    public const string FallbackTokenVariable = "GH_TOKEN";

    /// <summary>
    /// GitHub personal access token the tool sends as a bearer. Keep it out of a committed file
    /// (`appsettings.local.json`, or the environment variable instead). Not set: the variable named by
    /// `TokenVariable`, then `GH_TOKEN`.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Name of the environment variable holding the token when `Token` is empty; `GH_TOKEN` is read after
    /// it.
    /// </summary>
    public string TokenVariable { get; set; } = DefaultTokenVariable;

    /// <summary>
    /// The github tool's token: the first non-empty of the setting <c>Orkeon:Tools:GitHub:Token</c>, the
    /// environment variable named by <c>Orkeon:Tools:GitHub:TokenVariable</c> (default
    /// <see cref="DefaultTokenVariable"/>), then <see cref="FallbackTokenVariable"/>; <see langword="null"/>
    /// when none holds one.
    /// </summary>
    /// <param name="configuration">The host's configuration, or <see langword="null"/> when there is none.</param>
    /// <param name="readEnvironment">Reads one environment variable by name.</param>
    /// <returns>The token, or <see langword="null"/>.</returns>
    public static string? ResolveToken(IConfiguration? configuration, Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);

        var section = configuration?.GetSection(SectionName);
        var configured = section?[nameof(Token)];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        var variable = section?[nameof(TokenVariable)];
        if (string.IsNullOrWhiteSpace(variable))
            variable = DefaultTokenVariable;

        foreach (var name in new[] { variable.Trim(), FallbackTokenVariable })
        {
            var value = readEnvironment(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
