using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// Test double that approves any parseable URL. Represents the production DI-wired
/// <see cref="IUrlValidator"/> for tests that exercise HTTP plumbing rather than SSRF
/// policy, so they do not depend on real DNS resolution by the fail-closed default guard.
/// </summary>
internal sealed class AllowAllUrlValidator : IUrlValidator
{
    public Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default)
    {
        return Task.FromResult(
            url is not null && url.IsAbsoluteUri
                ? UrlValidationResult.Allowed(url)
                : UrlValidationResult.Denied("Invalid URL format"));
    }
}
