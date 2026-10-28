using System.Globalization;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>The grammar of a threshold written as text, such as <c>&gt;= 0.7</c>: an operator and a number.</summary>
public static class ThresholdExpression
{
    private static readonly (string Symbol, ConditionComparison Comparison)[] Operators =
    [
        (">=", ConditionComparison.GreaterOrEqual),
        ("<=", ConditionComparison.LessOrEqual),
        (">", ConditionComparison.Greater),
        ("<", ConditionComparison.Less),
    ];

    /// <summary>Reads <paramref name="text"/> as an operator followed by a number; false when it is neither.</summary>
    public static bool TryParse(string text, out ConditionComparison comparison, out double value)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        foreach (var (symbol, candidate) in Operators)
        {
            if (trimmed.StartsWith(symbol, StringComparison.Ordinal))
            {
                comparison = candidate;
                return double.TryParse(trimmed[symbol.Length..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && double.IsFinite(value);
            }
        }

        comparison = default;
        value = 0;
        return false;
    }
}
