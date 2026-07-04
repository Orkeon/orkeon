namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// Parses the <c>--repl-wrap [on|off]</c> flag from a runner's <c>args</c> array. Controls whether
/// the REPL history wraps long lines (no horizontal scrollbar) or shows the horizontal scrollbar.
/// Returns <c>null</c> when the flag is absent so callers can fall back to the default (wrap on).
/// </summary>
public static class ReplWrapFlagParser
{
    /// <summary>Parses a raw argv. Returns null when <c>--repl-wrap</c> is not present.</summary>
    public static bool? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--repl-wrap" && i + 1 < args.Length)
                return ParseValue(args[i + 1]);
            if (a.StartsWith("--repl-wrap=", StringComparison.Ordinal))
                return ParseValue(a[12..]);
        }
        return null;
    }

    /// <summary>Parses a single value string. Throws on unknown values.</summary>
    public static bool? ParseValue(string? value)
    {
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
        return value?.Trim().ToLowerInvariant() switch
        {
            "on" or "true" or "1" => true,
            "off" or "false" or "0" => false,
            _ => throw new ArgumentException($"Unknown --repl-wrap value: '{value}'. Expected: on|off.")
        };
#pragma warning restore CA1308
    }
}
