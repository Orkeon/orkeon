using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Default security policy that blocks DDL and destructive operations unless explicitly allowed.
/// </summary>
/// <remarks>
/// Defense-in-depth only. This policy strips SQL comments, splits stacked statements and inspects
/// each one, but it is NOT a substitute for least-privilege database accounts. The primary control
/// against destructive operations should be a read-only / scoped DB user; this policy is the
/// secondary, fail-closed barrier.
/// </remarks>
public sealed partial class DefaultDatabaseSecurityPolicy : IDatabaseSecurityPolicy
{
    // DDL keywords that are always blocked unless AllowDdl is true
    private static readonly string[] DdlKeywords = ["DROP", "TRUNCATE", "CREATE", "ALTER", "GRANT", "REVOKE"];

    /// <inheritdoc />
    public ValidationResult ValidateQuery(string query, DatabaseQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(query))
            return ValidationResult.Failure("Query", "Query cannot be empty.");

        // Neutralize comments before any analysis to defeat comment-prefix evasion (e.g. "/**/DELETE FROM t").
        var withoutComments = StripComments(query);

        var statements = SplitStatements(withoutComments);

        if (statements.Count == 0)
            return ValidationResult.Failure("Query", "Query cannot be empty.");

        if (!options.AllowMultiStatement && statements.Count > 1)
            return ValidationResult.Failure("Query", "Multi-statement queries are not allowed.");

        foreach (var statement in statements)
        {
            var normalized = NormalizeStatement(statement);
            if (normalized.Length == 0)
                continue;

            var violation = ValidateStatement(normalized, options);
            if (violation is not null)
                return ValidationResult.Failure("Query", violation);
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Applies the DDL and destructive-operation checks to a single normalized statement,
    /// honouring the configured allowances. Returns the violation message, or <c>null</c> when allowed.
    /// </summary>
    private static string? ValidateStatement(string normalized, DatabaseQueryOptions options)
    {
        if (!options.AllowDdl)
        {
            var ddlViolation = CheckDdl(normalized);
            if (ddlViolation is not null)
                return ddlViolation;
        }

        if (!options.AllowDestructiveDeletes)
        {
            var destructive = CheckDestructive(normalized);
            if (destructive is not null)
                return destructive;
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsDestructiveOperation(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var withoutComments = StripComments(query);

        foreach (var statement in SplitStatements(withoutComments))
        {
            var normalized = NormalizeStatement(statement);
            if (normalized.Length == 0)
                continue;

            if (CheckDdl(normalized) is not null || CheckDestructive(normalized) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes line comments (<c>-- ...</c>) and block comments (<c>/* ... */</c>) while preserving
    /// string literals, so comment markers inside quoted values are not stripped.
    /// </summary>
    private static string StripComments(string query)
    {
        var sb = new StringBuilder(query.Length);
        var i = 0;
        var len = query.Length;
        char? quote = null;

        while (i < len)
        {
            var c = query[i];

            if (quote is not null)
            {
                i = ConsumeQuotedChar(query, i, sb, ref quote);
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                sb.Append(c);
                i++;
                continue;
            }

            if (IsLineCommentStart(query, i))
            {
                i = SkipLineComment(query, i, sb);
                continue;
            }

            if (IsBlockCommentStart(query, i))
            {
                i = SkipBlockComment(query, i, sb);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends the current character of an open string literal, handling the doubled-quote escape
    /// (<c>''</c> / <c>""</c>) and closing the literal otherwise. Returns the next scan index.
    /// </summary>
    private static int ConsumeQuotedChar(string query, int i, StringBuilder sb, ref char? quote)
    {
        var c = query[i];
        sb.Append(c);
        if (c == quote!.Value)
        {
            // Handle doubled quote escape ('' or "")
            if (i + 1 < query.Length && query[i + 1] == quote.Value)
            {
                sb.Append(query[i + 1]);
                return i + 2;
            }
            quote = null;
        }
        return i + 1;
    }

    private static bool IsLineCommentStart(string query, int i) =>
        query[i] == '-' && i + 1 < query.Length && query[i + 1] == '-';

    /// <summary>Skips a <c>-- ...</c> line comment to end of line, emitting a single space. Returns the next scan index.</summary>
    private static int SkipLineComment(string query, int i, StringBuilder sb)
    {
        i += 2;
        while (i < query.Length && query[i] != '\n')
            i++;
        sb.Append(' ');
        return i;
    }

    private static bool IsBlockCommentStart(string query, int i) =>
        query[i] == '/' && i + 1 < query.Length && query[i + 1] == '*';

    /// <summary>Skips a <c>/* ... */</c> block comment, emitting a single space. Returns the next scan index.</summary>
    private static int SkipBlockComment(string query, int i, StringBuilder sb)
    {
        i += 2;
        while (i + 1 < query.Length && !(query[i] == '*' && query[i + 1] == '/'))
            i++;
        i += 2;
        sb.Append(' ');
        return i;
    }

    /// <summary>
    /// Splits a query into individual statements on top-level <c>;</c> separators,
    /// ignoring semicolons inside string literals. Empty statements are dropped.
    /// </summary>
    private static List<string> SplitStatements(string query)
    {
        var statements = new List<string>();
        var sb = new StringBuilder();
        char? quote = null;

        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];

            if (quote is not null)
            {
                AppendQuotedChar(query, ref i, sb, ref quote);
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                sb.Append(c);
                continue;
            }

            if (c == ';')
            {
                FlushStatement(sb, statements);
                continue;
            }

            sb.Append(c);
        }

        FlushStatement(sb, statements);

        return statements;
    }

    /// <summary>
    /// Appends the current character of an open string literal, consuming the doubled-quote escape
    /// (<c>''</c> / <c>""</c>) by advancing <paramref name="i"/>, or closing the literal otherwise.
    /// </summary>
    private static void AppendQuotedChar(string query, ref int i, StringBuilder sb, ref char? quote)
    {
        var c = query[i];
        sb.Append(c);
        if (c == quote!.Value)
        {
            if (i + 1 < query.Length && query[i + 1] == quote.Value)
            {
                sb.Append(query[i + 1]);
                i++;
                return;
            }
            quote = null;
        }
    }

    /// <summary>Appends the buffered statement to <paramref name="statements"/> when non-blank, then clears the buffer.</summary>
    private static void FlushStatement(StringBuilder sb, List<string> statements)
    {
        if (sb.ToString().Trim().Length > 0)
            statements.Add(sb.ToString());
        sb.Clear();
    }

    private static string NormalizeStatement(string statement)
    {
        return CollapseWhitespace().Replace(statement.Trim(), " ").ToUpperInvariant();
    }

    private static string? CheckDdl(string normalized)
    {
        foreach (var keyword in DdlKeywords)
        {
            // Match keyword at the start of the statement (statements are already split & normalized).
            if (normalized.StartsWith(keyword + " ", StringComparison.Ordinal) ||
                normalized == keyword)
            {
                return $"DDL operation '{keyword}' is not allowed.";
            }
        }

        // Also check for ALTER TABLE ... DROP inside a statement
        if (normalized.Contains("ALTER TABLE", StringComparison.Ordinal) && normalized.Contains("DROP", StringComparison.Ordinal))
            return "DDL operation 'ALTER TABLE DROP' is not allowed.";

        return null;
    }

    private static string? CheckDestructive(string normalized)
    {
        // DELETE without WHERE
        if (normalized.StartsWith("DELETE ", StringComparison.Ordinal) && !ContainsWhere(normalized))
            return "Destructive DELETE without WHERE clause is not allowed.";

        // UPDATE without WHERE
        if (normalized.StartsWith("UPDATE ", StringComparison.Ordinal) && !ContainsWhere(normalized))
            return "Destructive UPDATE without WHERE clause is not allowed.";

        // TRUNCATE is always destructive
        if (normalized.StartsWith("TRUNCATE ", StringComparison.Ordinal) || normalized == "TRUNCATE")
            return "Destructive TRUNCATE is not allowed.";

        return null;
    }

    private static bool ContainsWhere(string normalized)
    {
        return WhereClause().IsMatch(normalized);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();

    [GeneratedRegex(@"\bWHERE\b")]
    private static partial Regex WhereClause();
}
