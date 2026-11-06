using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// An <see cref="IUrlValidator"/> that refuses the hosts it is told to and allows every other one,
/// and records what it was asked.
/// </summary>
internal sealed class StubUrlValidator : IUrlValidator
{
    /// <summary>The hosts refused, whatever the path.</summary>
    public HashSet<string> RefusedHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a refusal says.</summary>
    public string Reason { get; set; } = "the host is private";

    /// <summary>Every address checked, in order.</summary>
    public List<string> Checked { get; } = [];

    public Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        lock (Checked) Checked.Add(url.AbsoluteUri);
        return Task.FromResult(RefusedHosts.Contains(url.Host) ? UrlValidationResult.Denied(Reason) : UrlValidationResult.Allowed(url));
    }
}
