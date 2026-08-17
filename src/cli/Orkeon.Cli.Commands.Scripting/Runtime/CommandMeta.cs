namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: surfaced to JS via Jint as ctx.command.{name,rawInput}
/// <summary>Metadata about the command being invoked, exposed as <c>ctx.command</c> in JS.</summary>
public sealed record CommandMeta(string name, string rawInput);
#pragma warning restore IDE1006
