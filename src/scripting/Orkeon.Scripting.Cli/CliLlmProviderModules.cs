using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Cli;

/// <summary>
/// The provider modules the <c>orkeon</c> tool ships, for the commands that build a provider without a
/// container (<c>orkeon doctor</c>, <c>orkeon llm probe</c>): the same list its host registers.
/// </summary>
internal static class CliLlmProviderModules
{
    /// <summary>The modules, by the key a section names them with in <c>Provider</c>.</summary>
    internal static IReadOnlyList<ILlmProviderModule> All { get; } = [];
}
