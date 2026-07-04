using System.Collections.Immutable;
using System.Globalization;

namespace Orkeon.Cli.Scripting.Args;

/// <summary>
/// Coerces raw parsed tokens into typed values, applies defaults, and enforces
/// per-<see cref="ArgSpec"/> constraints (required, choices, min, max).
/// </summary>
internal sealed class ArgsValidator
{
    /// <summary>Result of validation: either a typed values dictionary, or one or more error messages.</summary>
    public sealed record Result
    {
        public ImmutableDictionary<string, object?>? Values { get; init; }
        public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;
        public bool IsSuccess => Errors.IsDefaultOrEmpty || Errors.Length == 0;
    }

    public static Result Validate(
        ArgsTokenParser.Parsed parsed,
        IReadOnlyList<ArgSpec> schema)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(schema);

        var errors = ImmutableArray.CreateBuilder<string>();
        errors.AddRange(parsed.Errors);

        var output = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);

        foreach (var spec in schema)
        {
            object? typedValue;
            if (parsed.Values.TryGetValue(spec.Name, out var raw))
            {
                if (!TryCoerce(spec, raw, out typedValue, out var coerceError))
                {
                    errors.Add(coerceError!);
                    continue;
                }
                if (!CheckConstraints(spec, typedValue, out var constraintError))
                {
                    errors.Add(constraintError!);
                    continue;
                }
            }
            else
            {
                if (spec.Required)
                {
                    errors.Add($"Missing required argument '--{spec.Name}'.");
                    continue;
                }
                typedValue = DefaultFor(spec);
            }

            output[spec.Name] = typedValue;
        }

        return new Result
        {
            Values = errors.Count == 0 ? output.ToImmutable() : null,
            Errors = errors.ToImmutable(),
        };
    }

    private static bool TryCoerce(ArgSpec spec, object? raw, out object? value, out string? error)
    {
        value = null;
        error = null;

        switch (spec)
        {
            case StringArgSpec:
                value = raw?.ToString() ?? string.Empty;
                return true;

            case NumberArgSpec:
                return TryCoerceNumber(spec, raw, out value, out error);

            case BooleanArgSpec:
                return TryCoerceBool(spec, raw, out value, out error);

            case StringArrayArgSpec:
                if (raw is ImmutableArray<string> arr) { value = arr; return true; }
                if (raw is null) { value = ImmutableArray<string>.Empty; return true; }
                // Single positional/scalar to a string[] spec — wrap.
                value = ImmutableArray.Create(raw.ToString() ?? string.Empty);
                return true;

            default:
                error = $"Unsupported arg spec type {spec.GetType().Name}.";
                return false;
        }
    }

    private static bool TryCoerceNumber(ArgSpec spec, object? raw, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (raw is double d) { value = d; return true; }
        if (raw is null) { error = $"Argument '--{spec.Name}' (number) is missing a value."; return false; }
        if (double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        error = $"Argument '--{spec.Name}' (number) cannot parse '{raw}'.";
        return false;
    }

    private static bool TryCoerceBool(ArgSpec spec, object? raw, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (raw is bool b) { value = b; return true; }
        if (raw is null) { value = true; return true; }
        var s = raw.ToString() ?? string.Empty;
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase))
        { value = true; return true; }
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase))
        { value = false; return true; }
        error = $"Argument '--{spec.Name}' (boolean) cannot parse '{raw}'.";
        return false;
    }

    private static bool CheckConstraints(ArgSpec spec, object? typedValue, out string? error)
    {
        error = null;
        switch (spec)
        {
            case StringArgSpec ss when !ss.Choices.IsDefaultOrEmpty && ss.Choices.Length > 0:
            {
                var s = typedValue as string ?? string.Empty;
                if (!ss.Choices.Any(c => string.Equals(c, s, StringComparison.Ordinal)))
                {
                    error = $"Argument '--{spec.Name}' value '{s}' is not in choices [{string.Join(", ", ss.Choices)}].";
                    return false;
                }
                return true;
            }
            case NumberArgSpec ns:
            {
                var d = (double)typedValue!;
                if (ns.Min is { } min && d < min)
                {
                    error = $"Argument '--{spec.Name}' value {d} is below min {min}.";
                    return false;
                }
                if (ns.Max is { } max && d > max)
                {
                    error = $"Argument '--{spec.Name}' value {d} is above max {max}.";
                    return false;
                }
                return true;
            }
            default:
                return true;
        }
    }

    private static object? DefaultFor(ArgSpec spec) => spec switch
    {
        StringArgSpec s => s.Default,
        NumberArgSpec n => n.Default,
        BooleanArgSpec b => b.Default,
        StringArrayArgSpec a => a.Default ?? ImmutableArray<string>.Empty,
        _ => null,
    };
}
