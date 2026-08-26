using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// Scans content delegated between agents for PII, for a host that applies it at the agent
/// boundary.
/// <para>
/// <b>Nothing invokes this on its own.</b> The DLP interceptors are an opt-in toolkit, not a
/// pipeline stage: <c>AddOrkeonDlp()</c> registers them and a host that wants the channel
/// protected resolves <c>GetServices&lt;IDlpInterceptor&gt;()</c> and applies them itself.
/// The one-line summaries used to read as descriptions of what the framework does, which is
/// the opposite of true for a security control. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
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
