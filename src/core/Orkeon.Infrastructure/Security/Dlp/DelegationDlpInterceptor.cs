using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// DLP interceptor for delegation content.
/// Scans content being delegated between agents to prevent PII leaking across agent boundaries.
/// </summary>
public sealed class DelegationDlpInterceptor : BaseDlpInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DelegationDlpInterceptor"/> class.
    /// </summary>
    /// <param name="piiDetector">The PII detector used to scan delegation content.</param>
    public DelegationDlpInterceptor(IPiiDetector piiDetector) : base(piiDetector) { }

    /// <inheritdoc />
    public override DlpChannel Channel => DlpChannel.Delegation;
}
