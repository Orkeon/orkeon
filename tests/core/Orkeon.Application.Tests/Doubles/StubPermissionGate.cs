using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="IPermissionGate"/> that answers <see cref="Verdict"/> and keeps what it was asked.</summary>
internal sealed class StubPermissionGate : IPermissionGate
{
    public PermissionVerdict Verdict { get; set; } = PermissionVerdict.Allow();

    public List<(string Tool, string Mode)> Checks { get; } = [];

    public List<ToolAccess> Accesses { get; } = [];

    public Action? OnCheck { get; init; }

    /// <summary>When set, the verdict for a mode and an access class, in place of <see cref="Verdict"/>.</summary>
    public Func<string, ToolAccess, PermissionVerdict>? VerdictFor { get; init; }

    public System.Threading.Tasks.Task<PermissionVerdict> CheckAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
        ToolAccess declaredAccess = ToolAccess.Unspecified, CancellationToken cancellationToken = default)
    {
        Checks.Add((toolName, mode));
        Accesses.Add(declaredAccess);
        OnCheck?.Invoke();
        return System.Threading.Tasks.Task.FromResult(VerdictFor?.Invoke(mode, declaredAccess) ?? Verdict);
    }
}
