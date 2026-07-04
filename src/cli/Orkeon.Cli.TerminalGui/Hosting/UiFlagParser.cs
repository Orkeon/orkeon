namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// Parses the <c>--ui [tui|plain|auto]</c> flag from a runner's <c>args</c> array.
/// Returns <c>null</c> for "auto" (or absent) so callers feed it straight into
/// <see cref="TtyDetector.ResolveEffectiveMode"/>.
/// </summary>
public static class UiFlagParser
{
    /// <summary>Parses a raw argv. Removes consumed entries from the returned array if requested.</summary>
    public static UiMode? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--ui" && i + 1 < args.Length)
                return ParseValue(args[i + 1]);
            if (a.StartsWith("--ui=", StringComparison.Ordinal))
                return ParseValue(a[5..]);
        }
        return null;
    }

    /// <summary>Parses a single value string. Throws on unknown values.</summary>
    public static UiMode? ParseValue(string? value)
    {
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "auto" => null,
            "tui"   => UiMode.Tui,
            "plain" => UiMode.Plain,
            _ => throw new ArgumentException($"Unknown --ui value: '{value}'. Expected: tui|plain|auto.")
        };
#pragma warning restore CA1308
    }
}
