using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// Scans content for PII, for a host that applies it before storing in a memory provider.
/// <para>
/// <b>Nothing invokes this on its own.</b> The DLP interceptors are an opt-in toolkit, not a
/// pipeline stage: <c>AddOrkeonDlp()</c> registers them and a host that wants the channel
/// protected resolves <c>GetServices&lt;IDlpInterceptor&gt;()</c> and applies them itself.
/// The one-line summaries used to read as descriptions of what the framework does, which is
/// the opposite of true for a security control. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
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
