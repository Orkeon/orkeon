using System.Globalization;
using Orkeon.Constants.Configuration;

namespace Orkeon.Infrastructure.Session;

/// <summary>
/// The <c>Orkeon:Cli:Session</c> section: what a session budgets against. Bound and judged at the
/// host's start by <c>AddOrkeonSessionTools</c> (GAP-40) — <c>token_budget</c> used to read it at each
/// call and fall back on its default, in silence, for a value it could not read.
/// </summary>
public sealed class CliSessionOptions
{
    /// <summary>The configuration section: <c>Orkeon:Cli:Session</c>.</summary>
    public const string SectionName = "Orkeon:Cli:Session";

    /// <summary>The context window a session budgets against when none is configured, in tokens.</summary>
    public const int DefaultContextWindowTokens = 200_000;

    /// <summary>
    /// The model's context window, in tokens: a number above zero. Unset is
    /// <see cref="DefaultContextWindowTokens"/>.
    /// </summary>
    public int? ContextWindowTokens { get; set; }

    /// <summary>What a host says of a context window it cannot budget against, or null when it can.</summary>
    /// <param name="options">The bound options.</param>
    public static string? ContextWindowProblem(CliSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ContextWindowTokens is null or > 0
            ? null
            : $"{ConfigurationKeys.CliSessionContextWindowTokens} is '{options.ContextWindowTokens.Value.ToString(CultureInfo.InvariantCulture)}', " +
              $"which is not a context window: write a number of tokens above zero, such as {DefaultContextWindowTokens.ToString(CultureInfo.InvariantCulture)}.";
    }
}
