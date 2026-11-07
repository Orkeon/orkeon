using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Common;

/// <summary>
/// Reads an agent id given as text in a command: the ULID form that
/// <see cref="EntityId{TEntityId}.Parse"/> reads and <see cref="AgentId"/> prints, or the Guid
/// form <see cref="EntityId{TEntityId}.From(Guid)"/> reads. The validators and the handlers of
/// the commands that carry agent ids share it, so a value a validator accepts is always one its
/// handler can assign.
/// </summary>
internal static class AgentIdText
{
    /// <summary>Reads <paramref name="text"/> as a non-empty agent id.</summary>
    /// <param name="text">The agent id text.</param>
    /// <param name="id">The agent id when the text is one.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a non-empty ULID or Guid. A ULID is accepted only
    /// when it prints back as the same text, ignoring case: <c>Ulid.TryParse</c> decodes
    /// any 26 characters, including ones outside Crockford base32 and values that overflow
    /// 128 bits, into some other id.
    /// </returns>
    internal static bool TryParse(string? text, [NotNullWhen(true)] out AgentId? id)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            id = null;
            return false;
        }

        if (Ulid.TryParse(text, CultureInfo.InvariantCulture, out var ulid)
            && ulid != default
            && string.Equals(ulid.ToString(), text, StringComparison.OrdinalIgnoreCase))
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
