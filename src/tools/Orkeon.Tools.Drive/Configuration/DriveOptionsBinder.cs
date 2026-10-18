using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Drive.Configuration;

/// <summary>
/// Binds the <c>Orkeon:Tools:Drive</c> section without ever throwing — the e-mail family's
/// binder, spelt for drive accounts. The configuration binder throws on a value it cannot
/// convert (a misspelt right, a size written in words), and that exception would surface
/// wherever the options are first read: building the tool list, or the runner host itself, of
/// a crew that opens no drive. Each account is bound on its own instead,
/// and one holding such a value — or a key no account carries, which the binder would ignore
/// (GAP-40) — is kept, empty, with what is wrong: the account is reported as misconfigured when a
/// call or <c>orkeon drive</c> names it, and the others work. This is the one exception to the
/// rule that a host refuses at its start a setting it cannot honour.
/// </summary>
internal static class DriveOptionsBinder
{
    private const string AccountsKey = "Accounts";
    private const string WithholdRejectedKey = "Screening:WithholdRejected";

    /// <summary>
    /// Every account setting that is not a string, with its type: the values the binder converts,
    /// checked first so that each problem is named in the operator's terms.
    /// </summary>
    internal static readonly IReadOnlyList<(string Key, Type Type)> TypedAccountSettings =
    [
        ("Provider", typeof(DriveProvider?)),
        ("Rights", typeof(DriveRights)),
        ("Share:AllowPublicLinks", typeof(bool?)),
        ("MaxUploadMegabytes", typeof(int?)),
        ("TimeoutSeconds", typeof(int?)),
    ];

    /// <summary>Binds <paramref name="section"/> (the <c>Orkeon:Tools:Drive</c> section) into <paramref name="options"/>.</summary>
    public static void Bind(IConfiguration section, DriveToolsOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        options.DefaultAccount = section[nameof(DriveToolsOptions.DefaultAccount)];
        options.CredentialsDirectory = section[nameof(DriveToolsOptions.CredentialsDirectory)];

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
                options.SectionProblems.Add($"{Constants.DriveDefaults.SectionName}:{WithholdRejectedKey} '{withhold}' is neither true nor false");
            }
        }

        foreach (var child in section.GetSection(AccountsKey).GetChildren())
        {
            var account = new DriveAccountOptions();
            var problems = Check(child);
            if (problems.Count == 0)
            {
                try
                {
                    child.Bind(account);
                }
                catch (InvalidOperationException ex)
                {
                    problems.Add(ex.Message);
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
        CheckKeys(account, typeof(DriveAccountOptions), prefix: string.Empty, problems);
        foreach (var (key, type) in TypedAccountSettings)
        {
            var value = account[key];
            if (value is null)
                continue;

            var target = Nullable.GetUnderlyingType(type);
            if (target is not null && value.Length == 0)
                continue;

            if (!Converts(target ?? type, value))
                problems.Add(Describe(key, target ?? type, value));
        }

        return problems;
    }

    /// <summary>
    /// The keys of <paramref name="section"/> no property of <paramref name="type"/> carries, at any
    /// depth: <c>Roots</c> was read as absent, and the account opened the whole drive.
    /// </summary>
    private static void CheckKeys(IConfigurationSection section, Type type, string prefix, List<string> problems)
    {
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            var key = prefix + child.Key;
            if (!properties.TryGetValue(child.Key, out var propertyType))
            {
                var owner = prefix.Length == 0 ? "an account" : prefix.TrimEnd(':');
                problems.Add($"{key} is not an account setting: {owner} carries {string.Join(", ", properties.Keys)}");
                continue;
            }

            if (propertyType.IsClass && propertyType != typeof(string) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
                CheckKeys(child, propertyType, key + ":", problems);
        }
    }

    // The converter the configuration binder itself uses, so a value accepted here binds. A
    // number the enum does not define is refused too: the converter takes "5" for a security
    // mode that does not exist, which no later check would then recognise.
    private static bool Converts(Type type, string value)
    {
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

    private static bool IsDefined(Type type, Enum member)
    {
        if (!IsFlags(type))
            return Enum.IsDefined(type, member);

        var declared = Enum.GetValues(type).Cast<Enum>().Aggregate(0L, (all, flag) => all | Convert.ToInt64(flag, CultureInfo.InvariantCulture));
        return (Convert.ToInt64(member, CultureInfo.InvariantCulture) & ~declared) == 0;
    }

    private static string Describe(string key, Type type, string value)
    {
        if (type.IsEnum)
        {
            return IsFlags(type)
                ? $"{key} '{value}' is not a list of {Names(type)}, separated by commas"
                : $"{key} '{value}' is not one of {Names(type)}";
        }

        return type == typeof(bool)
            ? $"{key} '{value}' is neither true nor false"
            : $"{key} '{value}' is not a whole number";
    }

    private static bool IsFlags(Type type) => type.IsDefined(typeof(FlagsAttribute), inherit: false);

    // "None" of a flags enum grants nothing and is no answer to "what may an agent do".
    private static string Names(Type type) =>
        string.Join(", ", Enum.GetNames(type).Where(name => !(IsFlags(type) && name.Equals("None", StringComparison.Ordinal))));
}
