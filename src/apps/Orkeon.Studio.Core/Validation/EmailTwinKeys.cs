using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Validation;

/// <summary>
/// Keys equal but for the case, written by hand in one object of <c>Orkeon:Tools:Email</c>
/// (<c>Provider</c> and <c>provider</c>), judged as the JSON configuration reads them: it compares
/// keys without regard to case and refuses a value under a key it already holds — no run reads the
/// file, an error —, and reads as one two objects that set no same key — a warning, as Studio
/// reads and edits one of them only.
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

    /// <summary>
    /// The twin keys of <paramref name="owner"/> (at <paramref name="path"/>) and of every object
    /// under it, each key compared with every twin before it. Nothing is said of, nor under,
    /// <paramref name="accounts"/>: its keys are account names, which the validator judges, each
    /// account with them.
    /// </summary>
    public static IEnumerable<ValidationMessage> Under(JsonObject owner, string path, JsonObject? accounts = null)
    {
        foreach (var twins in owner.GroupBy(property => property.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            string? first = null;
            var read = new Reading();
            foreach (var (key, value) in twins)
            {
                var refused = read.Next(value);
                if (first is null)
                    first = key;
                else
                    yield return Finding(first, key, refused, $"{path}:{Spelling(first)}");
            }
        }

        foreach (var (key, value) in owner)
        {
            foreach (var finding in Below(value, $"{path}:{Spelling(key)}", accounts))
                yield return finding;
        }
    }

    private static IEnumerable<ValidationMessage> Below(JsonNode? node, string path, JsonObject? accounts)
    {
        switch (node)
        {
            case JsonObject container when !ReferenceEquals(container, accounts):
                return Under(container, path, accounts);
            case JsonArray array:
                return array.SelectMany((item, index) => Below(item, string.Create(CultureInfo.InvariantCulture, $"{path}:{index}"), accounts));
            default:
                return [];
        }
    }

    private static ValidationMessage Finding(string first, string key, string? refused, string path)
    {
        if (refused is null)
        {
            return ValidationMessage.Warning(
                ValidationCodes.EmailTwin,
                $"Keys '{first}' and '{key}' differ only by case: the run reads them as one, the keys of both together, " +
                "while Studio reads and edits one of them only. Merge them into one.",
                path);
        }

        var what = refused.Length == 0 ? "a value" : refused;
        return ValidationMessage.Error(
            ValidationCodes.EmailTwin,
            $"Keys '{first}' and '{key}' differ only by case and both set " +
            $"{what}: the configuration refuses a key written twice, " +
            "so no run can read this file. Keep one.",
            path);
    }

    private static string Spelling(string key) =>
        Spellings.FirstOrDefault(known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase)) ?? key;

    /// <summary>
    /// What the configuration holds of one key written in several cases, value after value. A
    /// value, a JSON null included, is refused under a key already held; an empty object or list
    /// holds its key too, but is refused nowhere — it replaces the value written before it.
    /// </summary>
    internal sealed class Reading
    {
        private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads <paramref name="node"/> after those before it: the first key under it the
        /// configuration refuses (empty for the node itself), or null when it merges the node in.
        /// </summary>
        public string? Next(JsonNode? node)
        {
            var keys = Leaves(node, string.Empty).ToList();
            var refused = keys.Where(key => key.Refusable).Select(key => key.Path).FirstOrDefault(_held.Contains);

            _held.UnionWith(keys.Select(key => key.Path));
            return refused;
        }

        /// <summary>The configuration keys <paramref name="node"/> sets, relative to it, and whether each is refused when already held.</summary>
        private static IEnumerable<(string Path, bool Refusable)> Leaves(JsonNode? node, string path) =>
            node switch
            {
                JsonObject { Count: > 0 } container => container.SelectMany(property => Leaves(property.Value, Under(path, property.Key))),
                JsonArray { Count: > 0 } array => array.SelectMany((item, index) =>
                    Leaves(item, Under(path, string.Create(CultureInfo.InvariantCulture, $"{index}")))),
                _ => [(path, node is not (JsonObject or JsonArray))],
            };

        /// <summary>The configuration path of <paramref name="key"/> under <paramref name="path"/>; the key itself at the root.</summary>
        private static string Under(string path, string key) =>
            path.Length == 0 ? key : $"{path}:{key}";
    }
}
