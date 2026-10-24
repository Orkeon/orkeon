using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

internal sealed class StubSequencePermissionGate : IPermissionGate
{
    private readonly IPermissionGate _inner;
    private readonly List<string> _log;
    private readonly Func<IReadOnlyDictionary<string, object?>, bool>? _denies;

    public StubSequencePermissionGate(IPermissionGate inner, List<string> log, Func<IReadOnlyDictionary<string, object?>, bool>? denies)
    {
        _inner = inner;
        _log = log;
        _denies = denies;
    }

    public List<string> Paths { get; } = new();

    public async Task<PermissionVerdict> CheckAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
        ToolAccess declaredAccess = ToolAccess.Unspecified, CancellationToken cancellationToken = default)
    {
        var path = arguments["path"]?.ToString() ?? "";
        lock (_log) _log.Add("gate:" + path);
        Paths.Add(path);
        if (_denies?.Invoke(arguments) == true)
            return PermissionVerdict.Deny("gate says no");
        return await _inner.CheckAsync(toolName, arguments, mode, declaredAccess, cancellationToken);
    }
}
