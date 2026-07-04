using System.Globalization;
using System.Text.RegularExpressions;
using Orkeon.Application.Common;

namespace Orkeon.Infrastructure.Templating;

/// <summary>
/// Evaluates template expressions including function calls, array access,
/// nested property access, and filter application.
/// Extracted from TemplateEngine to follow Single Responsibility Principle.
/// </summary>
internal sealed partial class ExpressionEvaluator
{
    private static readonly Regex FunctionPattern = FunctionPatternRegex();

    private readonly Dictionary<string, Func<object[], object>> _functions;
    private readonly Dictionary<string, Func<object, object[], object>> _filters;

    /// <summary>
    /// Initializes a new instance of <see cref="ExpressionEvaluator"/>.
    /// </summary>
    public ExpressionEvaluator(
        Dictionary<string, Func<object[], object>> functions,
        Dictionary<string, Func<object, object[], object>> filters)
    {
        _functions = functions;
        _filters = filters;
    }

    /// <summary>
    /// Evaluates a base expression (before filters) against the template parameters.
    /// </summary>
    public Task<object?> EvaluateBaseExpressionAsync(
        string expression,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken)
    {
        // Check for function call
        var functionMatch = FunctionPattern.Match(expression);
        if (functionMatch.Success)
        {
            var functionName = functionMatch.Groups[1].Value;
            var argsString = functionMatch.Groups[2].Value;

            if (_functions.TryGetValue(functionName, out var function))
            {
                var args = ArgumentParser.ParseArguments(argsString, parameters);
                return System.Threading.Tasks.Task.FromResult<object?>(function(args));
            }
        }

        // Check for array access (e.g., items[0])
        var arrayMatch = ArrayAccessRegex().Match(expression);
        if (arrayMatch.Success)
        {
            var arrayName = arrayMatch.Groups[1].Value;
            var index = int.Parse(arrayMatch.Groups[2].Value, CultureInfo.InvariantCulture);

            var array = parameters.Get<object>(arrayName);
            if (array is IList<object> list && index < list.Count)
            {
                return System.Threading.Tasks.Task.FromResult<object?>(list[index]);
            }
            else if (array is System.Collections.IList genericList && index < genericList.Count)
            {
                return System.Threading.Tasks.Task.FromResult<object?>(genericList[index]);
            }

            return System.Threading.Tasks.Task.FromResult<object?>(null);
        }

        // Check for nested property access (e.g., agent.name)
        if (expression.Contains('.', StringComparison.Ordinal) || expression.Contains('[', StringComparison.Ordinal))
        {
            return System.Threading.Tasks.Task.FromResult(GetNestedValue(expression, parameters));
        }

        // Simple parameter lookup
        var value = parameters.Get<object>(expression);
        if (value != null)
        {
            return System.Threading.Tasks.Task.FromResult<object?>(value);
        }

        return System.Threading.Tasks.Task.FromResult<object?>(null);
    }

    /// <summary>
    /// Applies a filter to a value.
    /// </summary>
    public object ApplyFilter(object? value, string filterExpression)
    {
        // For plain filter name like "upper", match directly
        var simpleFilterMatch = FilterExpressionRegex().Match(filterExpression);
        if (simpleFilterMatch.Success)
        {
            var filterName = simpleFilterMatch.Groups[1].Value;
            var argsString = simpleFilterMatch.Groups[2].Success ? simpleFilterMatch.Groups[2].Value : string.Empty;

            if (_filters.TryGetValue(filterName, out var filter))
            {
                var args = string.IsNullOrEmpty(argsString)
                    ? []
                    : ArgumentParser.ParseArguments(argsString, TemplateInstantiationParameters.Empty);

                return filter(value ?? string.Empty, args);
            }
        }

        return value ?? string.Empty;
    }

    /// <summary>
    /// Resolves a nested property path (e.g., "agent.name" or "items[0].value").
    /// </summary>
    private static object? GetNestedValue(string path, TemplateInstantiationParameters parameters)
    {
        var parts = path.Split('.');
        object? current = null;

        current = parameters.Get<object>(parts[0]);
        if (current != null)
        {
            for (int i = 1; i < parts.Length && current != null; i++)
            {
                var propName = parts[i];

                // Handle array access
                var arrayMatch = PropertyArrayAccessRegex().Match(propName);
                if (arrayMatch.Success)
                {
                    propName = arrayMatch.Groups[1].Value;
                    var index = int.Parse(arrayMatch.Groups[2].Value, CultureInfo.InvariantCulture);

                    current = GetPropertyValue(current, propName);
                    if (current is IList<object> list && index < list.Count)
                    {
                        current = list[index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    current = GetPropertyValue(current, propName);
                }
            }
        }

        return current;
    }

    /// <summary>
    /// Gets a property value from an object (supports dictionaries and reflection).
    /// </summary>
    private static object? GetPropertyValue(object obj, string propertyName)
    {
        if (obj is IDictionary<string, object> dict)
        {
            return dict.TryGetValue(propertyName, out var value) ? value : null;
        }

        var property = obj.GetType().GetProperty(propertyName);
        return property?.GetValue(obj);
    }

    [GeneratedRegex(@"(\w+)\s*\((.*?)\)")]
    private static partial Regex FunctionPatternRegex();

    [GeneratedRegex(@"^(\w+)\[(\d+)\]$")]
    private static partial Regex ArrayAccessRegex();

    [GeneratedRegex(@"^\s*(\w+)(?:\s*\((.*?)\))?\s*$")]
    private static partial Regex FilterExpressionRegex();

    [GeneratedRegex(@"(\w+)\[(\d+)\]")]
    private static partial Regex PropertyArrayAccessRegex();
}
