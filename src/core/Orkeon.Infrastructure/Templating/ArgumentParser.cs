using System.Globalization;
using System.Text;
using Orkeon.Application.Common;

namespace Orkeon.Infrastructure.Templating;

/// <summary>
/// Parses function/filter arguments from template expressions.
/// Extracted from TemplateEngine to support ExpressionEvaluator.
/// </summary>
internal static class ArgumentParser
{
    /// <summary>
    /// Parses a comma-separated argument string into an array of typed values.
    /// </summary>
    public static object[] ParseArguments(string argsString, TemplateInstantiationParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(argsString))
            return [];

        var args = new List<object>();
        var parts = SplitArguments(argsString);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            // String literal
            if ((trimmed.StartsWith('\'') && trimmed.EndsWith('\'')) ||
                (trimmed.StartsWith('"') && trimmed.EndsWith('"')))
            {
                args.Add(trimmed.Substring(1, trimmed.Length - 2));
            }
            // Number
            else if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
            {
                args.Add(number);
            }
            // Boolean
            else if (bool.TryParse(trimmed, out var boolean))
            {
                args.Add(boolean);
            }
            // Parameter reference
            else
            {
                var value = parameters.Get<object>(trimmed);
                if (value != null)
                {
                    args.Add(value);
                }
                else
                {
                    args.Add(trimmed);
                }
            }
        }

        return args.ToArray();
    }

    /// <summary>
    /// Splits a comma-separated string respecting quoted segments.
    /// </summary>
    public static List<string> SplitArguments(string argsString)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (int i = 0; i < argsString.Length; i++)
        {
            var c = argsString[i];

            if ((c == '\'' || c == '"') && (i == 0 || argsString[i - 1] != '\\'))
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (c == quoteChar)
                {
                    inQuotes = false;
                    quoteChar = '\0';
                }
            }

            if (c == ',' && !inQuotes)
            {
                args.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
