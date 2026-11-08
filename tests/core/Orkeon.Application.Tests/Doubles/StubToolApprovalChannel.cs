using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="IToolApprovalChannel"/> that answers <see cref="Decision"/> and keeps the requests.</summary>
internal sealed class StubToolApprovalChannel : IToolApprovalChannel
{
    public ToolApprovalDecision Decision { get; set; } = ToolApprovalDecision.AllowOnce;

    public List<ToolApprovalRequest> Requests { get; } = [];

    public Action? OnRequest { get; init; }

    public System.Threading.Tasks.Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        OnRequest?.Invoke();
        return System.Threading.Tasks.Task.FromResult(Decision);
    }
}
