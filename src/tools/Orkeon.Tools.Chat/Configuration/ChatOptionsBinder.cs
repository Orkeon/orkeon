using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;

namespace Orkeon.Tools.Chat.Configuration;

/// <summary>
/// Binds the <c>Orkeon:Tools:Chat</c> section without ever throwing — the e-mail family's
/// binder, spelt for chat accounts. The configuration binder throws on a value it cannot
/// convert (a misspelt right, a cap written in words), and that exception would surface
/// wherever the options are first read: building the tool list, or the runner host itself, of
/// a crew that opens no chat. Each account is bound on its own instead,
/// and one holding such a value — or a key no account carries, which the binder would ignore
/// (GAP-40) — is kept, empty, with what is wrong: the account is reported as misconfigured when a
/// call or <c>orkeon chat</c> names it, and the others work. This is the one exception to the
/// rule that a host refuses at its start a setting it cannot honour.
/// </summary>
internal static class ChatOptionsBinder
{
    private const string AccountsKey = "Accounts";
    private const string WithholdRejectedKey = "Screening:WithholdRejected";

    private static readonly KeyOwner AccountKeys = new(string.Empty, "an account setting", "an account");

    private static readonly KeyOwner SectionKeys = new(Constants.ChatDefaults.SectionName + ":", "a chat setting", Constants.ChatDefaults.SectionName);

    /// <summary>
    /// Every account setting that is not a string, with its type: the values the binder converts,
    /// checked first so that each problem is named in the operator's terms.
    /// </summary>
    internal static readonly IReadOnlyList<(string Key, Type Type)> TypedAccountSettings =
    [
        ("Provider", typeof(ChatProvider?)),
        ("Rights", typeof(ChatRights)),
        ("Auth:Method", typeof(ChatAuthMethod?)),
        ("Cloud", typeof(ChatCloud?)),
        ("Send:MaxPerHour", typeof(int?)),
    ];

    /// <summary>Binds <paramref name="section"/> (the <c>Orkeon:Tools:Chat</c> section) into <paramref name="options"/>.</summary>
    public static void Bind(IConfiguration section, ChatToolsOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        options.DefaultAccount = section[nameof(ChatToolsOptions.DefaultAccount)];
        options.CredentialsDirectory = section[nameof(ChatToolsOptions.CredentialsDirectory)];

        CheckKeys(section, typeof(ChatToolsOptions), prefix: string.Empty, SectionKeys, options.SectionProblems);

        var withhold = section[WithholdRejectedKey];
        if (!string.IsNullOrEmpty(withhold))
        {
            if (bool.TryParse(withhold.Trim(), out var parsed))
            {
                options.Screening.WithholdRejected = parsed;
            }
            else
            {
                // Unreadable, the stricter policy holds until an operator fixes the value.
                options.Screening.WithholdRejected = true;
                options.SectionProblems.Add($"{Constants.ChatDefaults.SectionName}:{WithholdRejectedKey} is neither true nor false");
            }
        }

        foreach (var child in section.GetSection(AccountsKey).GetChildren())
        {
            var account = new ChatAccountOptions();
            var problems = Check(child);
            if (problems.Count == 0)
            {
                try
                {
                    child.Bind(account);
                }
                catch (InvalidOperationException ex)
                {
                    problems.Add(Refusal(child.Key, ex));
                }
            }

            options.Accounts[child.Key] = account;
            if (problems.Count > 0)
                options.AccountProblems[child.Key] = problems;
        }
    }

    private static List<string> Check(IConfigurationSection account)
    {
        var problems = new List<string>();
        CheckKeys(account, typeof(ChatAccountOptions), prefix: string.Empty, AccountKeys, problems);
        foreach (var (key, type) in TypedAccountSettings)
        {
            var value = account[key];
            if (value is null)
                continue;

            var target = Nullable.GetUnderlyingType(type);
            // An empty name is an absent one; an empty cap is a guard rail someone meant to set.
            if (target is { IsEnum: true } && value.Length == 0)
                continue;

            if (!Converts(target ?? type, value))
                problems.Add(Describe(key, target ?? type, value));
        }

        return problems;
    }

    /// <summary>
    /// The keys of <paramref name="section"/> no property of <paramref name="type"/> carries, at any
    /// depth, and those that hold one value where several are expected: a misspelt guard rail must not
    /// read as an absent one.
    /// </summary>
    private static void CheckKeys(IConfiguration section, Type type, string prefix, KeyOwner root, List<string> problems)
    {
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            var key = prefix + child.Key;
            if (!properties.TryGetValue(child.Key, out var propertyType))
            {
                var owner = prefix.Length == 0 ? root.Name : prefix.TrimEnd(':');
                problems.Add($"{root.Prefix}{key} is not {root.Setting}: {owner} carries {string.Join(", ", properties.Keys)}");
                continue;
            }

            if (!propertyType.IsClass || propertyType == typeof(string))
                continue;

            var isList = typeof(System.Collections.IList).IsAssignableFrom(propertyType);
            if (!string.IsNullOrEmpty(child.Value))
                problems.Add($"{root.Prefix}{key} is a single value where a {(isList ? "list" : "section")} is expected");
            else if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
                CheckKeys(child, propertyType, key + ":", root, problems);
        }
    }

    /// <summary>How the refusal of a key names what it belongs to.</summary>
    private sealed record KeyOwner(string Prefix, string Setting, string Name);

    // The converter the configuration binder itself uses, so a value accepted here binds. A
    // number the enum does not define is refused too: the converter takes "5" for a security
    // mode that does not exist, which no later check would then recognise. A number is refused
    // even when the enum defines it (#464): a right is written by its name, never by its value.
    // A comma list is a combination of flags: on an enum that is not [Flags] the converter would
    // bind "SslOnConnect, StartTls" to whichever member the two values OR to, so it is refused.
    private static bool Converts(Type type, string value)
    {
        if (type.IsEnum && (HoldsNumber(value) || (!IsFlags(type) && value.Contains(',', StringComparison.Ordinal))))
            return false;

        object? converted;
        try
        {
            converted = TypeDescriptor.GetConverter(type).ConvertFromInvariantString(value);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        return converted is not Enum member || IsDefined(type, member);
    }

    private static bool HoldsNumber(string value) =>
        value.Split(',').Any(token => long.TryParse(token.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _));

    private static bool IsDefined(Type type, Enum member)
    {
        if (!IsFlags(type))
            return Enum.IsDefined(type, member);

        var declared = Enum.GetValues(type).Cast<Enum>().Aggregate(0L, (all, flag) => all | Convert.ToInt64(flag, CultureInfo.InvariantCulture));
        return (Convert.ToInt64(member, CultureInfo.InvariantCulture) & ~declared) == 0;
    }

    // No refusal repeats the value (#465): written in the wrong key, it may be a secret. The
    // sentence names the key and what it takes, in the words of the host's settings check.
    private static string Describe(string key, Type type, string value)
    {
        if (type.IsEnum)
        {
            if (IsFlags(type))
            {
                return HoldsNumber(value)
                    ? $"{key} holds a number where {type.Name} takes names: write one or more of {Names(type)}, separated by commas"
                    : $"{key} is not a list of {Names(type)}, separated by commas";
            }

            return HoldsNumber(value) ? $"{key} is a number where a name is expected: write one of {Names(type)}"
                : value.Contains(',', StringComparison.Ordinal) ? $"{key} is a list where a single name is expected: write one of {Names(type)}"
                : $"{key} is not one of {Names(type)}";
        }

        return type == typeof(bool)
            ? $"{key} is neither true nor false"
            : $"{key} is not a whole number";
    }

    private static bool IsFlags(Type type) => type.IsDefined(typeof(FlagsAttribute), inherit: false);

    // Every name the key accepts, None of a flags enum included, as the host's settings check lists them.
    private static string Names(Type type) => string.Join(", ", Enum.GetNames(type));

    /// <summary>
    /// The configuration binder's refusal of <paramref name="account"/>, without the value it quotes
    /// (#465), worded as the host's settings check words it (#469): a binder's sentence it cannot read
    /// back names the account's section instead.
    /// </summary>
    internal static string Refusal(string account, InvalidOperationException ex) =>
        BinderRefusal.WithoutValue(ex, $"{Constants.ChatDefaults.SectionName}:{AccountsKey}:{account}");
}
