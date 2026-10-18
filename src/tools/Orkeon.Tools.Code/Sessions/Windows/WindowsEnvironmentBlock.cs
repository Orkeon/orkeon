using System.Text;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// The environment block of <c>CreateProcessW</c> (<c>CREATE_UNICODE_ENVIRONMENT</c>): every
/// <c>NAME=value</c> followed by NUL, sorted by name without case as Windows expects, and a final
/// NUL. Pure.
/// </summary>
internal static class WindowsEnvironmentBlock
{
    /// <summary>The block of <paramref name="variables"/>.</summary>
    /// <exception cref="ArgumentException">A name is empty or holds <c>=</c> or NUL, or a value holds NUL.</exception>
    public static string Build(IEnumerable<KeyValuePair<string, string>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var builder = new StringBuilder();
        foreach (var (name, value) in variables.OrderBy(v => v.Key.ToUpperInvariant(), StringComparer.Ordinal))
        {
            if (name.Length == 0 || name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
                throw new ArgumentException($"'{name}' is not an environment variable name.", nameof(variables));
            if (value.Contains('\0', StringComparison.Ordinal))
                throw new ArgumentException($"The value of '{name}' holds a NUL character.", nameof(variables));
            builder.Append(name).Append('=').Append(value).Append('\0');
        }

        // An empty block still needs its two NULs.
        if (builder.Length == 0)
            builder.Append('\0');
        return builder.Append('\0').ToString();
    }
}
