using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Task.Commands.CreateTask;

/// <summary>
/// Reads the <see cref="CreateTaskCommand.AgentId"/> text: the ULID form that
/// <see cref="EntityId{TEntityId}.Parse"/> reads and <see cref="AgentId"/> prints, or the Guid
/// form <see cref="EntityId{TEntityId}.From(Guid)"/> reads. The validator and the handler share it,
/// so a value the validator accepts is always one the handler can assign.
/// </summary>
internal static class CreateTaskAgentId
{
    /// <summary>Reads <paramref name="text"/> as a non-empty agent id.</summary>
    /// <param name="text">The command's agent id text.</param>
    /// <param name="id">The agent id when the text is one.</param>
    /// <returns><see langword="true"/> when the text is a non-empty ULID or Guid.</returns>
    internal static bool TryParse(string text, [NotNullWhen(true)] out AgentId? id)
    {
        if (Ulid.TryParse(text, CultureInfo.InvariantCulture, out var ulid) && ulid != default)
        {
            id = AgentId.From(ulid);
            return true;
        }

        if (Guid.TryParse(text, CultureInfo.InvariantCulture, out var guid) && guid != Guid.Empty)
        {
            id = AgentId.From(guid);
            return true;
        }

        id = null;
        return false;
    }
}
