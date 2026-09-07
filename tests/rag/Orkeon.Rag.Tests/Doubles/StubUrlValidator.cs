using Orkeon.Domain.Tools.Security;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double: an <see cref="IUrlValidator"/> whose verdict is decided by
/// a predicate, so a test can deny one host without depending on real DNS
/// resolution. Records every URL it was asked about.
/// </summary>
public sealed class StubUrlValidator : IUrlValidator
{
    private readonly Func<Uri, bool> _isAllowed;
    private readonly string _denialReason;

    /// <summary>Initializes a validator allowing everything.</summary>
    public StubUrlValidator()
        : this(_ => true, "denied by test policy")
    {
    }

    /// <summary>Initializes a validator driven by <paramref name="isAllowed"/>.</summary>
    public StubUrlValidator(Func<Uri, bool> isAllowed, string denialReason)
    {
        ArgumentNullException.ThrowIfNull(isAllowed);
        _isAllowed = isAllowed;
        _denialReason = denialReason;
    }

    /// <summary>URLs submitted for validation, in order.</summary>
    public List<Uri> Validated { get; } = [];

    /// <inheritdoc />
    public Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        Validated.Add(url);

        return Task.FromResult(_isAllowed(url)
            ? UrlValidationResult.Allowed(url)
            : UrlValidationResult.Denied(_denialReason));
    }
}
