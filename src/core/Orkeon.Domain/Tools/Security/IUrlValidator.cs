namespace Orkeon.Domain.Tools.Security;

/// <summary>
/// Validates URLs to prevent Server-Side Request Forgery (SSRF) attacks.
/// </summary>
public interface IUrlValidator
{
    /// <summary>
    /// Validates a URL against security policies including scheme, host, port, and IP range checks.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="UrlValidationResult"/> indicating whether the URL is allowed.</returns>
    Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default);
}

/// <summary>
/// Result of URL validation containing the validation outcome and any denial reason.
/// </summary>
/// <param name="IsAllowed">Whether the URL is allowed.</param>
/// <param name="ValidatedUri">The parsed URI if allowed, or <see langword="null"/> if denied.</param>
/// <param name="DenialReason">The reason for denial, or <see langword="null"/> if allowed.</param>
public record UrlValidationResult(bool IsAllowed, Uri? ValidatedUri, string? DenialReason)
{
    /// <summary>Creates an allowed validation result.</summary>
    /// <param name="uri">The validated URI.</param>
    /// <returns>An allowed <see cref="UrlValidationResult"/>.</returns>
    public static UrlValidationResult Allowed(Uri uri) => new(true, uri, null);
    /// <summary>Creates a denied validation result.</summary>
    /// <param name="reason">The denial reason.</param>
    /// <returns>A denied <see cref="UrlValidationResult"/>.</returns>
    public static UrlValidationResult Denied(string reason) => new(false, null, reason);
}
