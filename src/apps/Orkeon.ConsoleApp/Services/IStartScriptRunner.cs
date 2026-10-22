namespace Orkeon.ConsoleApp.Services;

/// <summary>What a start script printed, and the source it ran from.</summary>
/// <param name="Stdout">The script's standard output.</param>
/// <param name="Source">The script's source text.</param>
internal sealed record StartScriptOutput(string Stdout, string Source);

/// <summary>Executes the REPL's start script (a <c>.ork.ts</c> or a shell script).</summary>
internal interface IStartScriptRunner
{
    /// <summary>Runs the script at <paramref name="scriptPath"/>, off the UI thread.</summary>
    Task<StartScriptOutput> RunAsync(string scriptPath, CancellationToken ct);
}
