using System.Collections.Immutable;

namespace Orkeon.Cli.Commands.Scripting.Args;

/// <summary>
/// Turns the raw <c>string[]</c> tokens produced by the runner into a dictionary of
/// <c>(argName → raw string value)</c>, ready for <see cref="ArgsValidator"/>.
/// </summary>
/// <remarks>
/// Supports the four token shapes from spec §13 Phase 3:
/// <list type="bullet">
///   <item><description><c>--flag</c> → <c>"true"</c> when the spec is boolean, otherwise an error.</description></item>
///   <item><description><c>--key=value</c> → <c>"value"</c>.</description></item>
///   <item><description><c>--key value</c> → consumes the next token as the value.</description></item>
///   <item><description><c>positional</c> → consumes the next non-flag arg spec in declaration order.</description></item>
/// </list>
/// String-array specs greedily consume tokens (positionally: until end; flag form:
/// until the next <c>--*</c>).
/// </remarks>
internal sealed class ArgsTokenParser
{
    /// <summary>Raw key-value bag returned by <see cref="Parse"/> — one entry per arg actually present in the tokens.</summary>
    public sealed record Parsed
    {
        public required ImmutableDictionary<string, object?> Values { get; init; }
        public required ImmutableArray<string> Errors { get; init; }
    }

    public static Parsed Parse(IReadOnlyList<string> tokens, IReadOnlyList<ArgSpec> schema)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(schema);

        var state = new ParseState
        {
            Tokens = tokens,
            ByName = schema.ToImmutableDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase),
            Values = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.OrdinalIgnoreCase),
            Errors = ImmutableArray.CreateBuilder<string>(),
            PositionalQueue = new Queue<ArgSpec>(schema),
        };

        while (state.Index < tokens.Count)
        {
            var tok = tokens[state.Index];
            if (tok.StartsWith("--", StringComparison.Ordinal))
                ParseNamedToken(state, tok);
            else
                ParsePositionalToken(state, tok);
        }

        return new Parsed
        {
            Values = state.Values.ToImmutable(),
            Errors = state.Errors.ToImmutable(),
        };
    }

    /// <summary>Mutable scratch state threaded through the token-parsing helpers.</summary>
    private sealed class ParseState
    {
        public required IReadOnlyList<string> Tokens { get; init; }
        public required ImmutableDictionary<string, ArgSpec> ByName { get; init; }
        public required ImmutableDictionary<string, object?>.Builder Values { get; init; }
        public required ImmutableArray<string>.Builder Errors { get; init; }
        public required Queue<ArgSpec> PositionalQueue { get; set; }
        public int Index { get; set; }
    }

    private static void ParseNamedToken(ParseState state, string tok)
    {
        // --key[=value]
        var trimmed = tok[2..];
        if (trimmed.Length == 0)
        {
            state.Errors.Add($"Unexpected token '--' at position {state.Index}.");
            state.Index++;
            return;
        }

        string keyName;
        string? inlineValue = null;
        var eqIdx = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (eqIdx >= 0)
        {
            keyName = trimmed[..eqIdx];
            inlineValue = trimmed[(eqIdx + 1)..];
        }
        else
        {
            keyName = trimmed;
        }

        if (!state.ByName.TryGetValue(keyName, out var spec))
        {
            state.Errors.Add($"Unknown option '--{keyName}'.");
            state.Index++;
            if (inlineValue is null && state.Index < state.Tokens.Count && !state.Tokens[state.Index].StartsWith("--", StringComparison.Ordinal))
                state.Index++; // skip its presumed value
            return;
        }

        switch (spec)
        {
            case BooleanArgSpec:
                state.Values[spec.Name] = inlineValue is null || ParseBool(inlineValue);
                state.Index++;
                return;
            case StringArrayArgSpec:
                ParseNamedArray(state, keyName, spec, inlineValue);
                return;
            default:
                ParseNamedScalar(state, keyName, spec, inlineValue);
                return;
        }
    }

    private static void ParseNamedArray(ParseState state, string keyName, ArgSpec spec, string? inlineValue)
    {
        var list = ImmutableArray.CreateBuilder<string>();
        if (inlineValue is not null) list.Add(inlineValue);
        state.Index++;
        while (state.Index < state.Tokens.Count && !state.Tokens[state.Index].StartsWith("--", StringComparison.Ordinal))
        {
            list.Add(state.Tokens[state.Index]);
            state.Index++;
        }
        if (list.Count == 0)
            state.Errors.Add($"Option '--{keyName}' expects at least one value.");
        else
            state.Values[spec.Name] = list.ToImmutable();
    }

    private static void ParseNamedScalar(ParseState state, string keyName, ArgSpec spec, string? inlineValue)
    {
        // string / number — single value
        string? raw = inlineValue;
        if (raw is null)
        {
            state.Index++;
            if (state.Index >= state.Tokens.Count || state.Tokens[state.Index].StartsWith("--", StringComparison.Ordinal))
            {
                state.Errors.Add($"Option '--{keyName}' requires a value.");
                return;
            }
            raw = state.Tokens[state.Index];
        }
        state.Values[spec.Name] = raw;
        state.Index++;

        // Once a named arg is used, that spec is no longer eligible as a positional.
        state.PositionalQueue = new Queue<ArgSpec>(
            state.PositionalQueue.Where(s => !string.Equals(s.Name, spec.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ParsePositionalToken(ParseState state, string tok)
    {
        // Positional: consume next un-named spec, skipping ones already filled.
        ArgSpec? target = null;
        while (state.PositionalQueue.Count > 0)
        {
            var head = state.PositionalQueue.Dequeue();
            if (state.Values.ContainsKey(head.Name)) continue;
            target = head;
            break;
        }
        if (target is null)
        {
            state.Errors.Add($"Unexpected positional argument '{tok}'.");
            state.Index++;
            return;
        }

        if (target is StringArrayArgSpec)
        {
            var list = ImmutableArray.CreateBuilder<string>();
            while (state.Index < state.Tokens.Count && !state.Tokens[state.Index].StartsWith("--", StringComparison.Ordinal))
            {
                list.Add(state.Tokens[state.Index]);
                state.Index++;
            }
            state.Values[target.Name] = list.ToImmutable();
        }
        else
        {
            state.Values[target.Name] = tok;
            state.Index++;
        }
    }

    private static bool ParseBool(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("1", StringComparison.Ordinal) ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);
}
