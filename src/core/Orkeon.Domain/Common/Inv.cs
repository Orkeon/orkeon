using System.Globalization;

namespace Orkeon.Domain.Common;

/// <summary>
/// Centralized Culture-Invariant formatting and parsing utilities.
/// Ensures consistent number, decimal, and date formatting regardless of system locale.
/// </summary>
public static class Inv
{
    /// <summary>
    /// Culture-invariant format provider for direct use with ToString/Parse methods.
    /// </summary>
    public static readonly IFormatProvider Culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Formats an interpolated string using invariant culture.
    /// Usage: Inv.Format($"{value:F4}")
    /// </summary>
    public static string Format(FormattableString formattable)
        => FormattableString.Invariant(formattable);

    // ── Numeric ToString ──────────────────────────────────────────

    /// <summary>Formats a <see cref="double"/> value using invariant culture and the given format.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The format string.</param>
    /// <returns>The formatted string.</returns>
    public static string ToString(double value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="float"/> value using invariant culture and the given format.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The format string.</param>
    /// <returns>The formatted string.</returns>
    public static string ToString(float value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="decimal"/> value using invariant culture and the given format.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The format string.</param>
    /// <returns>The formatted string.</returns>
    public static string ToString(decimal value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    // ── DateTime ToString ─────────────────────────────────────────

    /// <summary>Formats a <see cref="DateTime"/> value using invariant culture and the given format.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The format string.</param>
    /// <returns>The formatted string.</returns>
    public static string ToString(DateTime value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="DateTimeOffset"/> value using invariant culture and the given format.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The format string.</param>
    /// <returns>The formatted string.</returns>
    public static string ToString(DateTimeOffset value, string format)
        => value.ToString(format, CultureInfo.InvariantCulture);

    // ── Numeric Parsing ───────────────────────────────────────────

    /// <summary>Parses a <see cref="double"/> from a string using invariant culture.</summary>
    /// <param name="s">The string to parse.</param>
    /// <returns>The parsed value.</returns>
    public static double ParseDouble(string s)
        => double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>Tries to parse a <see cref="double"/> from a string using invariant culture.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="result">The parsed value if successful.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParseDouble(string s, out double result)
        => double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    /// <summary>Parses a <see cref="float"/> from a string using invariant culture.</summary>
    /// <param name="s">The string to parse.</param>
    /// <returns>The parsed value.</returns>
    public static float ParseFloat(string s)
        => float.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>Tries to parse a <see cref="float"/> from a string using invariant culture.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="result">The parsed value if successful.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParseFloat(string s, out float result)
        => float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    /// <summary>Parses a <see cref="decimal"/> from a string using invariant culture.</summary>
    /// <param name="s">The string to parse.</param>
    /// <returns>The parsed value.</returns>
    public static decimal ParseDecimal(string s)
        => decimal.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>Tries to parse a <see cref="decimal"/> from a string using invariant culture.</summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="result">The parsed value if successful.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParseDecimal(string s, out decimal result)
        => decimal.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    // ── Convert ───────────────────────────────────────────────────

    /// <summary>Converts an object to <see cref="double"/> using invariant culture.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static double ToDouble(object value)
        => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    /// <summary>Converts an object to <see cref="float"/> using invariant culture.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float ToFloat(object value)
        => Convert.ToSingle(value, CultureInfo.InvariantCulture);

    /// <summary>Converts an object to <see cref="decimal"/> using invariant culture.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static decimal ToDecimal(object value)
        => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}
