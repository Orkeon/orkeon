using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

internal sealed class FakeProbedTool : IBaseTool
{
    private readonly FakeRecordingTool _inner;
    private readonly FakeOverlapProbe _probe;

    public FakeProbedTool(string name, List<string> log, FakeOverlapProbe probe)
    {
        _inner = new FakeRecordingTool(name, log);
        _probe = probe;
    }

    public string Name => _inner.Name;
    public ToolAccess Access => _inner.Access;
    public string Description => _inner.Description;
    public ToolSchema Schema => _inner.Schema;

    public async Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        await _probe.InToolAsync().ConfigureAwait(false);
        return await _inner.CallAsync(request, cancellationToken);
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) => _inner.ExecuteAsync(input, cancellationToken);
    public bool ValidateInput(string input) => true;
}
