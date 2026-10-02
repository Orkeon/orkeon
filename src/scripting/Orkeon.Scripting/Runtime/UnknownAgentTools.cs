namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The names an agent's <c>.tools([...])</c> lists that the host's catalogue does not answer to,
/// with the catalogue itself (GAP-27). Resolved when the agent's context is built; <c>act</c>
/// refuses to run on them with an <see cref="Exceptions.UnknownToolException"/>.
/// </summary>
/// <param name="Names">The unknown names, as the agent wrote them, each once.</param>
/// <param name="Available">The names the host's catalogue offers, sorted.</param>
internal sealed record UnknownAgentTools(IReadOnlyList<string> Names, IReadOnlyList<string> Available);
