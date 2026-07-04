using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Application.Common;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Templating;

/// <summary>
/// Implementation of template engine with expression support.
/// Delegates parsing to TemplateParser, expression evaluation to ExpressionEvaluator,
/// and argument parsing to ArgumentParser.
/// Shared utility in Common/ — implements ITemplateEngine (Interfaces/Ports/) but not yet
/// consumed by any Application feature folder. Awaiting adoption.
/// </summary>
public partial class TemplateEngine : ITemplateEngine
{
    private readonly ILogger<TemplateEngine> _logger;
    private readonly Dictionary<string, Func<object[], object>> _functions = [];
    private readonly Dictionary<string, Func<object, object[], object>> _filters = [];
    private ExpressionEvaluator _evaluator;

    /// <summary>
    /// Initializes a new instance of <see cref="TemplateEngine"/>.
    /// </summary>
    public TemplateEngine(ILogger<TemplateEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        RegisterBuiltInFunctions();
        RegisterBuiltInFilters();
        _evaluator = new ExpressionEvaluator(_functions, _filters);
    }

    /// <summary>
    /// Render Async.
    /// </summary>
    public async Task<string> RenderAsync(
        string templateContent,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(templateContent))
            return string.Empty;

        var result = new StringBuilder(templateContent);
        var matches = TemplateParser.FindExpressions(templateContent);
        var replacements = new List<(int start, int length, string replacement)>();

        foreach (Match match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expression = match.Groups[1].Value.Trim();
            var value = await EvaluateExpressionAsync(expression, parameters, cancellationToken).ConfigureAwait(false);
            replacements.Add((match.Index, match.Length, value));
        }

        // Apply replacements in reverse order to maintain positions
        foreach (var (start, length, replacement) in replacements.OrderByDescending(r => r.start))
        {
            result.Remove(start, length);
            result.Insert(start, replacement);
        }

        return result.ToString();
    }

    /// <summary>
    /// Render With Inheritance Async.
    /// </summary>
    public async Task<string> RenderWithInheritanceAsync(
        string templateContent,
        string? baseTemplate,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(baseTemplate))
            return await RenderAsync(templateContent, parameters, cancellationToken).ConfigureAwait(false);

        // Merge templates (simple implementation - could be enhanced)
        var mergedTemplate = baseTemplate + "\n" + templateContent;
        return await RenderAsync(mergedTemplate, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extract Parameters Async.
    /// </summary>
    public Task<IEnumerable<string>> ExtractParametersAsync(
        string templateContent,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TemplateParser.ExtractParameters(templateContent));
    }

    /// <summary>
    /// Validate Template Async.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Validation barrier: any failure analyzing the template is recorded as a validation error (IsValid=false) so ValidateTemplateAsync always returns a result rather than throwing.")]
    public Task<TemplateValidation> ValidateTemplateAsync(
        string templateContent,
        CancellationToken cancellationToken = default)
    {
        var isValid = true;
        var errors = new List<string>();
        var parameters = new List<string>();

        try
        {
            var matches = TemplateParser.FindExpressions(templateContent);
            var parameterSet = new HashSet<string>();

            foreach (Match match in matches)
            {
                var expression = match.Groups[1].Value.Trim();

                // Validate expression syntax
                if (!TemplateParser.ValidateExpression(expression, out var error))
                {
                    isValid = false;
                    errors.Add($"Invalid expression '{{{{ {expression} }}}}': {error}");
                }

                // Extract parameter
                var paramMatch = BaseParameterNameRegex().Match(expression);
                if (paramMatch.Success)
                {
                    parameterSet.Add(paramMatch.Groups[1].Value);
                }
            }

            parameters = parameterSet.ToList();

            // Check for unclosed brackets
            var openCount = templateContent.Count(c => c == '{');
            var closeCount = templateContent.Count(c => c == '}');
            if (openCount != closeCount)
            {
                isValid = false;
                errors.Add("Mismatched brackets in template");
            }
        }
        catch (Exception ex)
        {
            isValid = false;
            errors.Add($"Template validation error: {ex.Message}");
        }

        var validation = new TemplateValidation
        {
            IsValid = isValid,
            Errors = errors,
            Parameters = parameters,
        };

        return Task.FromResult(validation);
    }

    /// <summary>
    /// Register Function.
    /// </summary>
    public void RegisterFunction(string name, Func<object[], object> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        _functions[name] = func;
        _evaluator = new ExpressionEvaluator(_functions, _filters);
    }

    /// <summary>
    /// Register Filter.
    /// </summary>
    public void RegisterFilter(string name, Func<object, object[], object> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filters[name] = filter;
        _evaluator = new ExpressionEvaluator(_functions, _filters);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Expression-evaluation barrier: any failure evaluating a template expression is logged and the original unchanged '{{ expression }}' placeholder is returned so one bad expression does not fail the whole template render.")]
    private async Task<string> EvaluateExpressionAsync(
        string expression,
        TemplateInstantiationParameters parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Split expression by filters
            var parts = expression.Split('|');
            var baseExpression = parts[0].Trim();

            // Evaluate base expression
            var value = await _evaluator.EvaluateBaseExpressionAsync(baseExpression, parameters, cancellationToken).ConfigureAwait(false);

            // Apply filters even if base value is null (for filters like 'default')
            for (int i = 1; i < parts.Length; i++)
            {
                var filterExpression = parts[i].Trim();
                value = _evaluator.ApplyFilter(value, filterExpression);
            }

            // If value is still null after filters, return unchanged template
            if (value == null)
            {
                LogExpressionEvaluationFailed(baseExpression);
                return $"{{{{ {expression} }}}}";
            }

            return ConvertToString(value);
        }
        catch (Exception ex)
        {
            LogExpressionEvaluationError(ex, expression);
            return $"{{{{ {expression} }}}}"; // Return unchanged on error
        }
    }

    private static string ConvertToString(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is IEnumerable<object> enumerable && !(value is string))
        {
            return string.Join(", ", enumerable);
        }

        return value.ToString() ?? string.Empty;
    }

    private void RegisterBuiltInFunctions()
    {
        // upper function
        RegisterFunction("upper", args => args.Length > 0 ? args[0]?.ToString()?.ToUpperInvariant() ?? string.Empty : string.Empty);

        // lower function
#pragma warning disable CA1308 // lowercase is the required produced output form of the 'lower' filter, not a comparison normalization
        RegisterFunction("lower", args => args.Length > 0 ? args[0]?.ToString()?.ToLowerInvariant() ?? string.Empty : string.Empty);
#pragma warning restore CA1308

        // default function
        RegisterFunction("default", args => args.Length >= 2 && (args[0] == null || string.IsNullOrEmpty(args[0]?.ToString())) ? args[1] : args[0]);

        // join function
        RegisterFunction("join", args =>
        {
            if (args.Length >= 2 && args[0] is IEnumerable<object> items)
            {
                var separator = args[1]?.ToString() ?? ", ";
                return string.Join(separator, items);
            }
            return args.FirstOrDefault() ?? string.Empty;
        });

        // count function
        RegisterFunction("count", args =>
        {
            if (args.Length > 0 && args[0] is IEnumerable<object> items)
            {
                return items.Count();
            }
            return 0;
        });
    }

    private void RegisterBuiltInFilters()
    {
        RegisterFilter("default", ApplyDefaultFilter);
        RegisterFilter("upper", ApplyUpperFilter);
        RegisterFilter("lower", ApplyLowerFilter);
        RegisterFilter("capitalize", ApplyCapitalizeFilter);
        RegisterFilter("truncate", ApplyTruncateFilter);
        RegisterFilter("join", ApplyJoinFilter);
    }

    private static object ApplyDefaultFilter(object value, object[] args)
    {
        return string.IsNullOrEmpty(value?.ToString()) && args.Length > 0 ? args[0] : value ?? string.Empty;
    }

    private static object ApplyUpperFilter(object value, object[] args)
    {
        return value?.ToString()?.ToUpperInvariant() ?? string.Empty;
    }

    private static object ApplyLowerFilter(object value, object[] args)
    {
#pragma warning disable CA1308 // lowercase is the required produced output form of the 'lower' filter, not a comparison normalization
        return value?.ToString()?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308
    }

    private static object ApplyCapitalizeFilter(object value, object[] args)
    {
        var str = value?.ToString() ?? string.Empty;
#pragma warning disable CA1308 // lowercase tail is the required produced output form of the 'capitalize' filter, not a comparison normalization
        return str.Length > 0 ? string.Concat(char.ToUpperInvariant(str[0]).ToString(), str[1..].ToLowerInvariant()) : str;
#pragma warning restore CA1308
    }

    private static object ApplyTruncateFilter(object value, object[] args)
    {
        var str = value?.ToString() ?? string.Empty;
        if (args.Length > 0 && int.TryParse(args[0].ToString(), out var length))
            return str.Length > length ? string.Concat(str.AsSpan(0, length), "...") : str;
        return str;
    }

    private static object ApplyJoinFilter(object value, object[] args)
    {
        if (value is IEnumerable<object> items)
        {
            var separator = args.Length > 0 ? args[0].ToString() : ", ";
            return string.Join(separator, items);
        }
        return value?.ToString() ?? string.Empty;
    }

    [GeneratedRegex(@"^([\w]+)")]
    private static partial Regex BaseParameterNameRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to evaluate expression: {Expression}")]
    private partial void LogExpressionEvaluationFailed(string expression);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to evaluate expression: {Expression}")]
    private partial void LogExpressionEvaluationError(Exception ex, string expression);
}
