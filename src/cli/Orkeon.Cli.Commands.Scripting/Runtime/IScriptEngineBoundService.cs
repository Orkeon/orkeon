namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// A whitelisted service that works through the script engine of the command that asks for it: it rejects with the
/// declared error classes and follows the run's token (#463). <see cref="ScriptServiceLocator"/> hands what
/// <see cref="Bind"/> returns, never the registered instance, to a command's <c>ctx.services.get</c>.
/// </summary>
internal interface IScriptEngineBoundService
{
    /// <summary>The service as one command run reads it.</summary>
    /// <param name="engine">The run's engine.</param>
    /// <param name="ct">The run's token (<c>ctx.signal</c>).</param>
    /// <returns>The object the script receives.</returns>
    object Bind(Jint.Engine engine, CancellationToken ct);
}
