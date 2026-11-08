using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The invocation point of the forge's own assistant: its calls are the operator's, made through the
/// forge they ran, so the permission gate does not stop them (FWK-06). The guardian, the truncation, the
/// sanitizer and the audit still apply: only <see cref="ToolInvocationCaller.GateExempt"/> is set.
/// </summary>
internal sealed class GateExemptToolInvocationPipeline(IToolInvocationPipeline inner) : IToolInvocationPipeline
{
    /// <inheritdoc />
    /// <remarks>Answers for the wrapped pipeline: the script facade then leaves its own gate off, and the calls are exempt.</remarks>
    public bool GatesPermissions => inner.GatesPermissions;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
        ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return inner.InvokeAsync(invocation with { Caller = invocation.Caller with { GateExempt = true } }, cancellationToken);
    }
}
