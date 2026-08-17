using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Orkeon.Infrastructure.Serialization;

/// <summary>
/// Forgiving deserialization-side type inspector that accepts both camelCase and snake_case
/// YAML keys for the same C# property. The canonical (serialization) form remains camelCase.
///
/// Experiment 07 friction #2: legacy crew YAML uses snake_case (<c>expected_output:</c>,
/// <c>output_file:</c>, <c>async_execution:</c>); strict camelCase matching produced a hard
/// parse error on those crews. This inspector wraps the standard inspector and, when a YAML
/// key cannot be resolved as-is, retries with a snake_case → camelCase conversion before
/// honoring the caller's <c>ignoreUnmatched</c> contract.
/// </summary>
internal sealed partial class CamelOrSnakeCaseTypeInspector : ITypeInspector
{
    private readonly ITypeInspector _inner;
    private readonly ILogger? _logger;

    public CamelOrSnakeCaseTypeInspector(ITypeInspector inner, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _logger = logger;
    }

    public IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container)
        => _inner.GetProperties(type, container);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Tolerant property matching: a failure resolving the camelCase form is swallowed so the inspector can retry the snake_case fallback and ultimately re-issue the original lookup honoring the caller's ignoreUnmatched contract.")]
    public IPropertyDescriptor GetProperty(
        Type type,
        object? container,
        string name,
        bool ignoreUnmatched,
        bool caseInsensitivePropertyMatching)
    {
        // Fast path: the YAML key already matches camelCase (the canonical form).
        try
        {
            return _inner.GetProperty(type, container, name, ignoreUnmatched: false, caseInsensitivePropertyMatching);
        }
        catch
        {
            // Fallback path: treat the YAML key as snake_case and retry with the camelCase
            // equivalent. Avoid the retry when the conversion would be a no-op.
            var camel = SnakeToCamelCase(name);
            if (!string.Equals(camel, name, StringComparison.Ordinal))
            {
                try
                {
                    var resolved = _inner.GetProperty(type, container, camel, ignoreUnmatched: false, caseInsensitivePropertyMatching);
                    if (_logger is not null)
                        LogSnakeCaseMatched(_logger, name, camel, type.Name);
                    return resolved;
                }
                catch
                {
                    // Fall through to the original failure surface so the caller's
                    // ignoreUnmatched contract is honored uniformly.
                }
            }

            return _inner.GetProperty(type, container, name, ignoreUnmatched, caseInsensitivePropertyMatching);
        }
    }

    public string GetEnumName(Type enumType, string name) => _inner.GetEnumName(enumType, name);

    public string GetEnumValue(object enumValue) => _inner.GetEnumValue(enumValue);

    public bool HasParseMethod(Type type) => _inner.HasParseMethod(type);

    public object? Parse(string value, Type expectedType) => _inner.Parse(value, expectedType);

    /// <summary>
    /// Converts <c>expected_output</c> → <c>expectedOutput</c>. Idempotent on already-camelCase
    /// input; leaves leading uppercase intact (matches YamlDotNet's CamelCase convention which
    /// only lowercases the first char of PascalCase property names).
    /// </summary>
    internal static string SnakeToCamelCase(string snake)
    {
        if (string.IsNullOrEmpty(snake) || !snake.Contains('_', StringComparison.Ordinal))
            return snake;

        var sb = new StringBuilder(snake.Length);
        var upperNext = false;
        foreach (var c in snake)
        {
            if (c == '_')
            {
                upperNext = true;
                continue;
            }

            sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }
        return sb.ToString();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Matched snake_case YAML key '{Key}' to camelCase property '{Camel}' on {Type}")]
    static partial void LogSnakeCaseMatched(ILogger logger, string key, string camel, string type);
}
