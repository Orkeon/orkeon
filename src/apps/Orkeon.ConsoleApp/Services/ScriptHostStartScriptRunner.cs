using System.Text.Json;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Scripting;
using Orkeon.Tools.Code;

namespace Orkeon.ConsoleApp.Services;

/// <summary>
/// Runs the start script on a background thread: a <c>.ork.ts</c> through <see cref="ScriptHost"/>,
/// any other file as a shell script through the <c>shell_command</c> tool.
/// </summary>
internal sealed class ScriptHostStartScriptRunner : IStartScriptRunner
{
    private const string ScriptSuffix = ".ork.ts";
    private readonly ScriptHost _scriptHost;
    private readonly IFileSystemService _fileSystem;
    private readonly IEnumerable<IBaseTool> _tools;

    public ScriptHostStartScriptRunner(ScriptHost scriptHost, IFileSystemService fileSystem, IEnumerable<IBaseTool> tools)
    {
        _scriptHost = scriptHost;
        _fileSystem = fileSystem;
        _tools = tools;
    }

    public Task<StartScriptOutput> RunAsync(string scriptPath, CancellationToken ct)
        => Task.Run(() => RunCoreAsync(scriptPath, ct), ct);

    private async Task<StartScriptOutput> RunCoreAsync(string scriptPath, CancellationToken ct)
    {
        var source = await _fileSystem.TryReadAllTextAsync(scriptPath, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Start script not found: {scriptPath}");

        if (scriptPath.EndsWith(ScriptSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var printed = new System.Collections.Concurrent.ConcurrentQueue<string>();
            await _scriptHost.RunAsync(scriptPath, printed.Enqueue, ct).ConfigureAwait(false);
            return new StartScriptOutput(string.Join('\n', printed), source);
        }

        var shell = _tools.FirstOrDefault(t => t.Name == "shell_command")
            ?? throw new InvalidOperationException("The shell_command tool is not registered.");
        var input = JsonSerializer.Serialize(new ShellCommandRequest { Command = $"sh {scriptPath}" });
        var result = await shell.ExecuteAsync(input, ct).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "The start script failed.");

        var stdout = result.Output ?? string.Empty;
        try
        {
            stdout = JsonSerializer.Deserialize<ShellCommandResponse>(stdout, JsonSerializerOptions.Web)?.Stdout ?? stdout;
        }
        catch (JsonException)
        {
            // The output was plain text: keep it as it is.
        }

        return new StartScriptOutput(stdout, source);
    }
}
