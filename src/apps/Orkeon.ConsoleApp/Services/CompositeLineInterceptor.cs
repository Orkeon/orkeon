using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.ConsoleApp.Services;

/// <summary>
/// The REPL's one interceptor, offering each line to its members in order; the first with a question waiting names
/// the prompt. It is also the tool-approval channel, answering through the member that is one, so the gate, the
/// channel and the loop's line hook stay a single chain.
/// </summary>
internal sealed class CompositeLineInterceptor(IReplLineInterceptor approvals, IReplLineInterceptor other)
    : IReplLineInterceptor, IToolApprovalChannel
{
    public string? PromptOverride => approvals.PromptOverride ?? other.PromptOverride;

    public bool TryConsume(string line) => approvals.TryConsume(line) || other.TryConsume(line);

    public Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
        => ((IToolApprovalChannel)approvals).RequestAsync(request, cancellationToken);
}
