using System.Text.RegularExpressions;
using Orkeon.Application.Abstractions.Data;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.Data;

/// <summary>
/// Default security policy that blocks DDL and destructive operations unless explicitly allowed.
/// </summary>
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

        var normalized = NormalizeQuery(query);

        if (!options.AllowDdl)
        {
            var ddlViolation = CheckDdl(normalized);
            if (ddlViolation is not null)
                return ValidationResult.Failure("Query", ddlViolation);
        }

        if (!options.AllowDestructiveDeletes && IsDestructiveDelete(normalized))
            return ValidationResult.Failure("Query", "Destructive DELETE without WHERE clause is not allowed.");

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public bool IsDestructiveOperation(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalized = NormalizeQuery(query);
        return CheckDdl(normalized) is not null || IsDestructiveDelete(normalized);
    }

    private static string NormalizeQuery(string query)
    {
        // Collapse whitespace and trim for reliable keyword matching
        return CollapseWhitespace().Replace(query.Trim(), " ").ToUpperInvariant();
    }

    private static string? CheckDdl(string normalized)
    {
        foreach (var keyword in DdlKeywords)
        {
            // Match keyword at the start of the query or as a standalone word
            if (normalized.StartsWith(keyword + " ", StringComparison.Ordinal) ||
                normalized.StartsWith(keyword + ";", StringComparison.Ordinal) ||
                normalized == keyword)
            {
                return $"DDL operation '{keyword}' is not allowed.";
            }
        }

        // Also check for ALTER TABLE ... DROP inside compound statements
        if (normalized.Contains("ALTER TABLE", StringComparison.Ordinal) && normalized.Contains("DROP", StringComparison.Ordinal))
            return "DDL operation 'ALTER TABLE DROP' is not allowed.";

        return null;
    }

    private static bool IsDestructiveDelete(string normalized)
    {
        // DELETE without WHERE
        if (normalized.StartsWith("DELETE ", StringComparison.Ordinal) &&
            !normalized.Contains("WHERE", StringComparison.Ordinal))
            return true;

        // TRUNCATE is always destructive
        if (normalized.StartsWith("TRUNCATE ", StringComparison.Ordinal) ||
            normalized == "TRUNCATE")
            return true;

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();
}
