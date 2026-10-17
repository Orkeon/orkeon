using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Validation;

/// <summary>
/// Keys equal but for the case, written by hand in one object of <c>Orkeon:Tools:Email</c>
/// (<c>Provider</c> and <c>provider</c>), judged as the JSON configuration reads them: it compares
/// keys without regard to case and refuses a value under a key it already holds — no run reads the
/// file, an error —, and reads as one two objects that set no same key — a warning, as Studio
/// reads and edits one of them only. The check is <see cref="TwinKeys"/>'; this names the keys.
/// </summary>
internal static class EmailTwinKeys
{
    /// <summary>The keys Studio knows in the section, whatever the object that carries them, as it spells them.</summary>
    private static readonly string[] Spellings =
    [
        EmailSection.Keys.DefaultAccount, EmailSection.Keys.CredentialsDirectory, EmailSection.Keys.Accounts,
        EmailSection.Keys.Screening, EmailSection.Keys.WithholdRejected,
        .. EmailAccountRules.AccountKeys, .. EmailAccountRules.ObjectKeys.Values.SelectMany(keys => keys),
    ];

    private static readonly TwinKeys Check = new(Spellings, ValidationCodes.EmailTwin);

    /// <summary>
    /// The twin keys of <paramref name="owner"/> (at <paramref name="path"/>) and of every object
    /// under it; see <see cref="TwinKeys.Under"/>.
    /// </summary>
    public static IEnumerable<ValidationMessage> Under(JsonObject owner, string path, JsonObject? accounts = null) =>
        Check.Under(owner, path, accounts);
}
