using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Minimal <see cref="HttpToolBase"/> built without an <see cref="IUrlValidator"/>, so that
/// <c>ValidateUrlAsync</c> falls back to the fail-closed default SSRF guard of
/// Orkeon.Tools.Abstractions. It exists so a single test can compare that guard's verdicts
/// with the full <c>UrlValidator</c>'s and catch any drift between their range tables.
/// </summary>
public sealed class StubDefaultGuardHttpTool : HttpToolBase
{
    public StubDefaultGuardHttpTool() : base((HttpClient?)null) { }

    /// <summary>Exposes the protected default-guard validation to the tests.</summary>
    public Task<UrlValidationResult> GuardAsync(Uri url, CancellationToken ct = default)
        => ValidateUrlAsync(url, ct);

    protected override Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new ToolCallResponse(true, null, null));
}
