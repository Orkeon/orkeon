namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// The names of <c>Orkeon:Tools:Shell:Environment:Propagate</c> that passed
/// <see cref="ShellEnvironment.SelectPropagated"/>: filtered once per host, so a refused name is
/// warned about once, not at every tool the registry builds.
/// </summary>
/// <param name="Names">The accepted names, in their configured order.</param>
internal sealed record ShellPropagatedVariables(IReadOnlyList<string> Names);
