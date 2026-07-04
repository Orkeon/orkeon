using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// Base implementation for DLP channel interceptors.
/// </summary>
public abstract class BaseDlpInterceptor : IDlpInterceptor
{
    private readonly IPiiDetector _piiDetector;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseDlpInterceptor"/> class.
    /// </summary>
    /// <param name="piiDetector">The PII detector used to scan content for personally identifiable information.</param>
    protected BaseDlpInterceptor(IPiiDetector piiDetector)
    {
        _piiDetector = piiDetector;
    }

    /// <inheritdoc />
    public abstract DlpChannel Channel { get; }

    /// <inheritdoc />
    public Task<DlpInterceptionResult> InterceptAsync(string content, DlpPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.Enabled)
            return Task.FromResult(new DlpInterceptionResult(DlpAction.Allow, content, content, Array.Empty<PiiMatch>()));

        var scanResult = _piiDetector.Scan(content);
        if (!scanResult.HasPii)
            return Task.FromResult(new DlpInterceptionResult(DlpAction.Allow, content, content, scanResult.Matches));

        var action = DetermineAction(scanResult.Matches, policy);
        var processedContent = action switch
        {
            DlpAction.Block => $"[BLOCKED: PII detected in {Channel} channel]",
            DlpAction.Mask => _piiDetector.Mask(content, scanResult.Matches),
            DlpAction.Audit => content,
            _ => content
        };

        return Task.FromResult(new DlpInterceptionResult(action, content, processedContent, scanResult.Matches));
    }

    private static DlpAction DetermineAction(IReadOnlyList<PiiMatch> matches, DlpPolicy policy)
    {
        var highestAction = policy.DefaultAction;

        foreach (var match in matches)
        {
            if (policy.PiiActions.TryGetValue(match.Type, out var piiAction) && piiAction > highestAction)
            {
                highestAction = piiAction;
            }
        }

        return highestAction;
    }
}
