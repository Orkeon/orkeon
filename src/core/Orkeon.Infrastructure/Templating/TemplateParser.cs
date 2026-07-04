using System.Text.RegularExpressions;

namespace Orkeon.Infrastructure.Templating;

/// <summary>
/// Handles template parsing, variable extraction, and syntax validation.
/// Extracted from TemplateEngine to follow Single Responsibility Principle.
/// </summary>
internal static partial class TemplateParser
{
    private static readonly Regex ParameterPattern = ParameterPatternRegex();

    /// <summary>
    /// Gets the compiled parameter pattern for matching {{ expressions }}.
    /// </summary>
    public static Regex Pattern => ParameterPattern;

    /// <summary>
    /// Extracts all parameter names from a template.
    /// </summary>
    public static IEnumerable<string> ExtractParameters(string template)
    {
        var parameters = new HashSet<string>();
        var matches = ParameterPattern.Matches(template);

        foreach (Match match in matches)
        {
            var expression = match.Groups[1].Value.Trim();

            // Extract base parameter name (before any filters or array access)
            var paramMatch = BaseParameterNameRegex().Match(expression);
            if (paramMatch.Success)
            {
                parameters.Add(paramMatch.Groups[1].Value);
            }
        }

        return parameters;
    }

    /// <summary>
    /// Validates an expression for balanced parentheses and brackets.
    /// </summary>
    public static bool ValidateExpression(string expression, out string? error)
    {
        error = null;

        // Check for balanced parentheses
        var openParens = expression.Count(c => c == '(');
        var closeParens = expression.Count(c => c == ')');
        if (openParens != closeParens)
        {
            error = "Unbalanced parentheses";
            return false;
        }

        // Check for balanced brackets
        var openBrackets = expression.Count(c => c == '[');
        var closeBrackets = expression.Count(c => c == ']');
        if (openBrackets != closeBrackets)
        {
            error = "Unbalanced brackets";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Finds all expression matches in a template.
    /// </summary>
    public static MatchCollection FindExpressions(string template)
    {
        return ParameterPattern.Matches(template);
    }

    [GeneratedRegex(@"\{\{(.*?)\}\}")]
    private static partial Regex ParameterPatternRegex();

    [GeneratedRegex(@"^([\w]+)")]
    private static partial Regex BaseParameterNameRegex();
}
