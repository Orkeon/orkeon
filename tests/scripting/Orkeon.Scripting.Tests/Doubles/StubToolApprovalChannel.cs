using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// An <see cref="IToolApprovalChannel"/> that records every question it is asked and answers a
/// configured decision (<see cref="ToolApprovalDecision.AllowOnce"/> by default).
/// </summary>
internal sealed class StubToolApprovalChannel : IToolApprovalChannel
{
    /// <summary>The decision returned to every ask.</summary>
    public ToolApprovalDecision Decision = ToolApprovalDecision.AllowOnce;

    /// <summary>Every request received, in order.</summary>
    public List<ToolApprovalRequest> Asks { get; } = new();

    public Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        lock (Asks) Asks.Add(request);
        return Task.FromResult(Decision);
    }
}
