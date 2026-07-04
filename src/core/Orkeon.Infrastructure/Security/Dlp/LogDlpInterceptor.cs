using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// DLP interceptor for log messages.
/// Scans log messages before writing to ensure PII never appears in logs.
/// </summary>
public sealed class LogDlpInterceptor : BaseDlpInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogDlpInterceptor"/> class.
    /// </summary>
    /// <param name="piiDetector">The PII detector used to scan log content.</param>
    public LogDlpInterceptor(IPiiDetector piiDetector) : base(piiDetector) { }

    /// <inheritdoc />
    public override DlpChannel Channel => DlpChannel.Log;
}
