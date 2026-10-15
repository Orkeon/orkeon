using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="IToolApprovalChannel"/> that answers every request with <see cref="Decision"/> and keeps
/// the requests it was asked, so a test reads what the gate put to the operator.
/// </summary>
public sealed class StubToolApprovalChannel : IToolApprovalChannel
{
    /// <summary>The answer to every request.</summary>
    public ToolApprovalDecision Decision { get; set; } = ToolApprovalDecision.AllowOnce;

    /// <summary>The requests received, in order.</summary>
    public List<ToolApprovalRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Decision);
    }
}
