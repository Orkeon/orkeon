using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IGuardian with call tracking and configurable results.
/// </summary>
public class MockGuardian : IGuardian
{
    private GuardResult _result = GuardResult.Allow();

    // --- Tracking ---
    public int CheckCallCount { get; private set; }
    public GuardContext? LastCheckContext { get; private set; }
    public List<GuardContext> AllCheckContexts { get; } = [];

    // --- Configuration ---
    public void SetCheckResult(GuardResult result) => _result = result;

    public void SetAllow() => _result = GuardResult.Allow();

    public void SetBlock(string reason, IReadOnlyList<GuardViolation>? violations = null) =>
        _result = GuardResult.Block(reason, violations ?? Array.Empty<GuardViolation>());

    public void SetWarn(string reason, IReadOnlyList<GuardViolation>? violations = null) =>
        _result = GuardResult.Warn(reason, violations ?? Array.Empty<GuardViolation>());

    // --- IGuardian ---
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        CheckCallCount++;
        LastCheckContext = context;
        AllCheckContexts.Add(context);
        return Task.FromResult(_result);
    }
}
