using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// DLP interceptor for tool output content.
/// Scans tool execution results before they reach the agent.
/// </summary>
public sealed class ToolOutputDlpInterceptor : BaseDlpInterceptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolOutputDlpInterceptor"/> class.
    /// </summary>
    /// <param name="piiDetector">The PII detector used to scan tool output.</param>
    public ToolOutputDlpInterceptor(IPiiDetector piiDetector) : base(piiDetector) { }

    /// <inheritdoc />
    public override DlpChannel Channel => DlpChannel.ToolOutput;
}
