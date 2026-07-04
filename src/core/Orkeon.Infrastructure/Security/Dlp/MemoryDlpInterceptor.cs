using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// DLP interceptor for memory storage.
/// Scans content before storing in memory providers to prevent PII from persisting.
/// </summary>
public sealed class MemoryDlpInterceptor : BaseDlpInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryDlpInterceptor"/> class.
    /// </summary>
    /// <param name="piiDetector">The PII detector used to scan memory content.</param>
    public MemoryDlpInterceptor(IPiiDetector piiDetector) : base(piiDetector) { }

    /// <inheritdoc />
    public override DlpChannel Channel => DlpChannel.Memory;
}
