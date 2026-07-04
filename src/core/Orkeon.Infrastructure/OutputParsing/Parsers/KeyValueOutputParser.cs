using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Parsers;

/// <summary>
/// Shared key-value parsing helpers.
/// Avoids duplicating static fields across generic type instantiations (S2743).
/// </summary>
internal static partial class KeyValueOutputParserDefaults
{
    [GeneratedRegex(@"^\s*(?<key>[^:=\->]+?)\s*(?::|\s*=\s*|\s*->\s*)\s*(?<value>.+)$", RegexOptions.Multiline)]
    internal static partial Regex KvPattern();

    internal static Dictionary<string, string> ExtractKeyValuePairs(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lines = text.Split('\n');
        string? currentKey = null;
        var currentValue = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var match = KvPattern().Match(line);
            if (match.Success)
            {
                // Save previous key-value pair if any
                if (currentKey != null)
                {
                    result[currentKey] = currentValue.ToString().Trim();
                }

                currentKey = match.Groups["key"].Value.Trim();
                currentValue.Clear();
                currentValue.Append(match.Groups["value"].Value.Trim());
            }
            else if (currentKey != null && !string.IsNullOrWhiteSpace(line))
            {
                // Continuation of a multiline value
                currentValue.AppendLine();
                currentValue.Append(line.Trim());
            }
        }

        // Add last pair
        if (currentKey != null)
        {
            result[currentKey] = currentValue.ToString().Trim();
        }

        return result;
    }
}

/// <summary>
/// Parses key-value formatted output such as "Key: Value\nKey2: Value2".
/// Supports various separators: ":", "=", "->".
/// Can map to Dictionary or to typed object properties.
/// </summary>
public sealed class KeyValueOutputParser<T> : IStructuredOutputParser<T> where T : class
{
    /// <inheritdoc />
    public T Parse(string llmOutput)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);

        var pairs = KeyValueOutputParserDefaults.ExtractKeyValuePairs(llmOutput);

        if (typeof(T) == typeof(Dictionary<string, string>))
        {
            return (T)(object)pairs;
        }

        return MapToObject(pairs);
    }

    /// <inheritdoc />
    public bool TryParse(string llmOutput, out T? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(llmOutput))
            return false;

        try
        {
            result = Parse(llmOutput);
            return result != null;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<T> ParseAsync(string llmOutput, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Parse(llmOutput));
    }

    private static T MapToObject(Dictionary<string, string> pairs)
    {
        var obj = Activator.CreateInstance<T>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

        foreach (var pair in pairs)
        {
            var prop = properties.FirstOrDefault(p =>
                string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, pair.Key.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));

            if (prop != null)
            {
                try
                {
                    // LLM/key-value output uses invariant formatting (e.g. "9.5" with a dot
                    // decimal). Convert culture-invariantly so a comma-decimal host culture
                    // doesn't silently drop numeric fields to default(T) via the catch below.
                    var value = Convert.ChangeType(pair.Value, prop.PropertyType, CultureInfo.InvariantCulture);
                    prop.SetValue(obj, value);
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Skip properties that can't be converted
                }
            }
        }

        return obj;
    }
}

/// <summary>
/// Non-generic key-value parser that returns Dictionary&lt;string, string&gt;.
/// </summary>
public sealed class KeyValueOutputParser : IStructuredOutputParser
{
    /// <inheritdoc />
    public object? Parse(string llmOutput, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(llmOutput);

        var pairs = KeyValueOutputParserDefaults.ExtractKeyValuePairs(llmOutput);

        if (targetType == typeof(Dictionary<string, string>))
            return pairs;

        // Map to target type via reflection
        var obj = Activator.CreateInstance(targetType);
        if (obj == null) return null;

        var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

        foreach (var pair in pairs)
        {
            var prop = properties.FirstOrDefault(p =>
                string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, pair.Key.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));

            if (prop != null)
            {
                try
                {
                    // LLM/key-value output uses invariant formatting (e.g. "9.5" with a dot
                    // decimal). Convert culture-invariantly so a comma-decimal host culture
                    // doesn't silently drop numeric fields to default(T) via the catch below.
                    var value = Convert.ChangeType(pair.Value, prop.PropertyType, CultureInfo.InvariantCulture);
                    prop.SetValue(obj, value);
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Skip properties that can't be converted
                }
            }
        }

        return obj;
    }

    /// <inheritdoc />
    public bool TryParse(string llmOutput, Type targetType, out object? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(llmOutput))
            return false;

        try
        {
            result = Parse(llmOutput, targetType);
            return result != null;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }
}
