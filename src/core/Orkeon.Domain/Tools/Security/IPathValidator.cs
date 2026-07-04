namespace Orkeon.Domain.Tools.Security;

/// <summary>
/// Validates file paths for security, preventing directory traversal and access to sensitive files.
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// Validates a requested path against security rules.
    /// </summary>
    /// <param name="requestedPath">The path to validate.</param>
    /// <param name="workspaceRoot">Optional workspace root override. If null, uses the configured default.</param>
    /// <returns>A result indicating whether the path is allowed and the resolved absolute path.</returns>
    PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null);
}

/// <summary>
/// Result of a path validation check.
/// </summary>
public record PathValidationResult(
    bool IsAllowed,
    string? ResolvedPath,
    string? DenialReason)
{
    /// <summary>
    /// Creates an allowed result with the resolved path.
    /// </summary>
    public static PathValidationResult Allowed(string resolvedPath) => new(true, resolvedPath, null);

    /// <summary>
    /// Creates a denied result with the reason for denial.
    /// </summary>
    public static PathValidationResult Denied(string reason) => new(false, null, reason);
}
