using Orkeon.Domain.SharedKernel;

namespace Orkeon.Tools.Abstractions.Data;

/// <summary>
/// Security policy for validating database queries before execution.
/// </summary>
public interface IDatabaseSecurityPolicy
{
    /// <summary>
    /// Validates a SQL query against the security policy.
    /// </summary>
    /// <param name="query">The SQL query to validate.</param>
    /// <param name="options">Query options controlling what operations are allowed.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating whether the query is allowed.</returns>
    ValidationResult ValidateQuery(string query, DatabaseQueryOptions options);

    /// <summary>
    /// Checks whether the query contains a destructive operation (DELETE without WHERE, TRUNCATE, DROP).
    /// </summary>
    /// <param name="query">The SQL query to check.</param>
    /// <returns><c>true</c> if the query is destructive; otherwise <c>false</c>.</returns>
    bool IsDestructiveOperation(string query);
}
