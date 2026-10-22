using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

internal sealed class FakeRecordingTool : IBaseTool
{
    private readonly List<string> _log;

    public FakeRecordingTool(string name, List<string> log)
    {
        Name = name;
        _log = log;
    }

    public string Name { get; }
    public ToolAccess Access => ToolAccess.Read;
    public string Description => "test tool";
    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
    public List<string> Inputs { get; } = new();

    public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        var path = request.Parameters["path"]?.ToString() ?? "";
        Inputs.Add(path);
        lock (_log) _log.Add("tool:" + path);
        return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
    }

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(ToolResult.CreateSuccess("ok"));

    public bool ValidateInput(string input) => true;
}
