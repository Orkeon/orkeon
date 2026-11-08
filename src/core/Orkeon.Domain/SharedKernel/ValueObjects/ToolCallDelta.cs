namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// One fragment of a tool call a model streams while it builds the call.
/// </summary>
/// <param name="Index">Position of the call in the turn (the provider's own index; 0 for the first).</param>
/// <param name="Id">The call's id, on the fragment that carries it; null otherwise.</param>
/// <param name="Name">The tool's name, on the fragment that carries it; null otherwise.</param>
/// <param name="ArgumentsDelta">The piece of the JSON arguments this fragment adds; empty when it carries none.</param>
public sealed record ToolCallDelta(int Index, string? Id, string? Name, string ArgumentsDelta);
