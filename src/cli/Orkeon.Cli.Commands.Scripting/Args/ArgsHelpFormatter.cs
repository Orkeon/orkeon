using System.Collections.Immutable;
using System.Text;

namespace Orkeon.Cli.Commands.Scripting.Args;

/// <summary>
/// Renders a multi-line, aligned help block for a single scripted command.
/// Consumed by the <c>help-cmd</c> command (see <c>ScriptHelpCommand</c>).
/// </summary>
public static class ArgsHelpFormatter
{
    /// <summary>
    /// Produces the help block. <paramref name="schema"/> may be empty.
    /// </summary>
    public static string Format(string name, IReadOnlyList<string> aliases, string description, ImmutableArray<ArgSpec> schema)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var sb = new StringBuilder();
        sb.Append(name);
        if (aliases.Count > 0)
            sb.Append(" (").Append(string.Join(", ", aliases)).Append(')');
        sb.Append(" — ").Append(description);
        sb.AppendLine();

        if (schema.IsDefaultOrEmpty || schema.Length == 0)
        {
            sb.AppendLine("  (no declared args; handler receives { raw: string[] })");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("Arguments:");

        var keyWidth = schema.Max(s => $"--{s.Name}".Length);
        var typeWidth = schema.Max(s => s.Kind.Length);

        foreach (var spec in schema)
        {
            var head = $"--{spec.Name}".PadRight(keyWidth);
            var kind = spec.Kind.PadRight(typeWidth);
            sb.Append("  ").Append(head).Append("  ").Append(kind).Append("  ").Append(DescribeConstraints(spec)).AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string DescribeConstraints(ArgSpec spec)
    {
        var parts = new List<string>();
        if (spec.Required) parts.Add("required");
        switch (spec)
        {
            case StringArgSpec s:
                AppendStringConstraints(s, parts);
                break;
            case NumberArgSpec n:
                AppendNumberConstraints(n, parts);
                break;
            case BooleanArgSpec b:
                if (b.Default is { } v)
#pragma warning disable CA1308 // lowercase is the required display/wire form ("true"/"false"), not a comparison normalization
                    parts.Add($"default: {v.ToString().ToLowerInvariant()}");
#pragma warning restore CA1308

                break;
            case StringArrayArgSpec a:
                if (a.Default is { } def)
                    parts.Add($"default: [{string.Join(", ", def)}]");
                break;
        }
        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static void AppendStringConstraints(StringArgSpec s, List<string> parts)
    {
        if (!string.IsNullOrEmpty(s.Default)) parts.Add($"default: \"{s.Default}\"");
        if (!s.Choices.IsDefaultOrEmpty && s.Choices.Length > 0)
            parts.Add($"choices: {string.Join(" | ", s.Choices)}");
    }

    private static void AppendNumberConstraints(NumberArgSpec n, List<string> parts)
    {
        if (n.Default is { } d) parts.Add($"default: {d}");
        if (n.Min is { } min) parts.Add($"min: {min}");
        if (n.Max is { } max) parts.Add($"max: {max}");
    }
}
