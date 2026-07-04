using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// DLP interceptor for external output.
/// Scans final output before returning to users or external systems -- last line of defense.
/// </summary>
public sealed class ExternalOutputDlpInterceptor : BaseDlpInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalOutputDlpInterceptor"/> class.
    /// </summary>
    /// <param name="piiDetector">The PII detector used to scan external output.</param>
    public ExternalOutputDlpInterceptor(IPiiDetector piiDetector) : base(piiDetector) { }

    /// <inheritdoc />
    public override DlpChannel Channel => DlpChannel.ExternalOutput;
}
