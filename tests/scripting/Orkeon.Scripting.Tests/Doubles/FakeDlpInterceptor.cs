using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>A DLP interceptor that masks every occurrence of <see cref="Secret"/> and records what it was asked to scan.</summary>
public sealed class FakeDlpInterceptor : IDlpInterceptor
{
    /// <summary>The text that is masked wherever it appears.</summary>
    public string Secret = "sk-SECRET-123";

    /// <summary>Every content the interceptor scanned, in order.</summary>
    public List<string> Scanned { get; } = [];

    public DlpChannel Channel => DlpChannel.ExternalOutput;

    public Task<DlpInterceptionResult> InterceptAsync(string content, DlpPolicy policy, CancellationToken ct = default)
    {
        Scanned.Add(content);
        var masked = content.Replace(Secret, "[MASKED]", StringComparison.Ordinal);
        return Task.FromResult(new DlpInterceptionResult(masked == content ? DlpAction.Allow : DlpAction.Mask, content, masked, []));
    }
}
