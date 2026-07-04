namespace Orkeon.Cli.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: this record is consumed from JS via Jint as a CLR object
/// <summary>
/// Returned by <c>handler(args, ctx)</c> via <c>ctx.continue()</c> / <c>ctx.exit()</c>.
/// Mapped to <c>Orkeon.Cli.Abstractions.Commands.CommandResult</c> by <c>ScriptCommand</c>.
/// </summary>
/// <param name="exit">True ⇒ runner exits its loop after this command.</param>
/// <param name="message">Optional message displayed by the runner.</param>
public sealed record CommandActionResult(bool exit, string? message);
#pragma warning restore IDE1006
