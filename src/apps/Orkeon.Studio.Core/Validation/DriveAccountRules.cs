using System.Globalization;
using System.Net.Mail;
using System.Text.Json.Nodes;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Studio.Core.Configuration;
using static Orkeon.Studio.Core.Configuration.DriveSection.Values;

namespace Orkeon.Studio.Core.Validation;

/// <summary>
/// The rules the engine applies to a drive account, spelt again in Core on the model of
/// <see cref="EmailAccountRules"/>: what <c>orkeon run</c> will say of the account when a tool or
/// <c>orkeon drive</c> names it. A broken account stops no run — the engine sets it aside, with its
/// problems —, so every finding is a warning, in the engine's own sentence, at the key that fixes it.
/// </summary>
/// <remarks>
/// Core does not reference <c>Orkeon.Tools.Drive</c>: the sentences and the order of the checks
/// are copies of <c>DriveOptionsBinder</c> and <c>DriveAccountResolver</c>, and
/// <c>DriveValidationOracleTests</c> holds each of them to the real binder and the real resolver.
/// </remarks>
public static class DriveAccountRules
{
    /// <summary>The largest upload the engine accepts, in megabytes.</summary>
    internal const int MaxUploadMegabytesCeiling = 10 * 1024;

    /// <summary>The upload limit of an account that names none, in megabytes.</summary>
    internal const int DefaultMaxUploadMegabytes = 100;

    /// <summary>The tenant of a OneDrive account that names none: personal Microsoft accounts.</summary>
    internal const string DefaultTenant = "consumers";

    /// <summary>The longest account name the engine accepts.</summary>
    private const int MaxNameLength = 64;

    private const string ClientIdKey = DriveSection.Keys.Auth + ":" + DriveSection.Keys.ClientId;
    private const string ClientSecretEnvVarKey = DriveSection.Keys.Auth + ":" + DriveSection.Keys.ClientSecretEnvVar;
    private const string TenantKey = DriveSection.Keys.Auth + ":" + DriveSection.Keys.Tenant;
    private const string AllowedRecipientsKey = DriveSection.Keys.Share + ":" + DriveSection.Keys.AllowedRecipients;
    private const string AllowPublicLinksKey = DriveSection.Keys.Share + ":" + DriveSection.Keys.AllowPublicLinks;
    private const string WithholdRejectedPath =
        DriveSection.SectionPath + ":" + DriveSection.Keys.Screening + ":" + DriveSection.Keys.WithholdRejected;

    /// <summary>How the binder reads a setting that is not a string.</summary>
    private enum Kind
    {
        Provider,
        Rights,
        Integer,
        Boolean,
    }

    /// <summary>
    /// Every account setting the binder converts, in the order it checks them. An optional one may
    /// be empty, which reads as absent; <c>Rights</c> may not.
    /// </summary>
    private static readonly (string Key, Kind Kind, bool Optional)[] TypedSettings =
    [
        (DriveSection.Keys.Provider, Kind.Provider, true),
        (DriveSection.Keys.Rights, Kind.Rights, false),
        (AllowPublicLinksKey, Kind.Boolean, true),
        (DriveSection.Keys.MaxUploadMegabytes, Kind.Integer, true),
        (DriveSection.Keys.TimeoutSeconds, Kind.Integer, true),
    ];

    /// <summary>The keys an account carries, in the order the engine declares them.</summary>
    internal static readonly string[] AccountKeys =
    [
        DriveSection.Keys.Provider, DriveSection.Keys.Rights, DriveSection.Keys.Root, DriveSection.Keys.Auth,
        DriveSection.Keys.Share, DriveSection.Keys.MaxUploadMegabytes, DriveSection.Keys.TimeoutSeconds,
    ];

    /// <summary>The keys each object of an account carries, by the object's key.</summary>
    internal static readonly Dictionary<string, string[]> ObjectKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        [DriveSection.Keys.Auth] = [DriveSection.Keys.ClientId, DriveSection.Keys.ClientSecretEnvVar, DriveSection.Keys.Tenant],
        [DriveSection.Keys.Share] = [DriveSection.Keys.AllowedRecipients, DriveSection.Keys.AllowPublicLinks],
    };

    /// <summary>
    /// The findings of an account as <paramref name="definition"/> holds it, under
    /// <paramref name="name"/>: empty when the engine will use the account. A value the engine
    /// cannot read (a provider it does not know, rights that are no list of rights) is all it says
    /// of the account, as the engine then reads nothing else of it.
    /// </summary>
    /// <remarks>
    /// One finding is Studio's own and not the engine's: a <c>ClientSecretEnvVar</c> that cannot be
    /// the name of a variable — the secret pasted in the field that names it. The engine resolves
    /// such an account, and fails when it reads the secret.
    /// </remarks>
    public static IReadOnlyList<ValidationMessage> Check(string name, DriveAccountDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);

        var findings = new Findings(name);
        foreach (var (key, kind, optional) in TypedSettings)
        {
            if (Unreadable(key, kind, optional, WrittenText(definition, key)) is { } problem)
                findings.Add(ValidationCodes.DriveValue, key, problem);
        }

        if (findings.Messages.Count == 0)
            Resolve(name, definition, findings);

        return findings.Messages;
    }

    /// <summary>
    /// The findings of the account <paramref name="name"/> (its exact spelling) as the file holds
    /// it, which is what the run reads: a key no account carries and a value the binder cannot
    /// convert first — with the screening switch of the section when it is unreadable, which sets
    /// every account aside —, and the engine then says nothing else of the account; otherwise the
    /// rules of <see cref="Check(string, DriveAccountDefinition)"/>. Keys equal but for the case
    /// (<see cref="DriveTwinKeys"/>) come before all of it and alone. Empty when the file holds no
    /// such account.
    /// </summary>
    public static IReadOnlyList<ValidationMessage> Check(AppSettingsDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(name);

        if (document.Drive.AccountsNode is not { } accounts || !accounts.TryGetPropertyValue(name, out var node))
            return [];

        // What stands under the name may be no object at all: the engine then reads an account
        // that declares nothing.
        var account = node as JsonObject;
        if (account is not null && DriveTwinKeys.Under(account, $"{DriveSection.AccountsPath}:{name}").ToList() is { Count: > 0 } twins)
            return twins;

        var findings = new Findings(name);
        if (ScreeningProblem(document) is { } screening)
            findings.Add(ValidationCodes.DriveScreening, string.Empty, screening);

        if (account is not null)
        {
            UnknownKeys(account, AccountKeys, prefix: string.Empty, findings);
            foreach (var (key, kind, optional) in TypedSettings)
            {
                if (Unreadable(key, kind, optional, EmailAccountRules.ConfigurationText(EmailAccountRules.Child(account, key))) is { } problem)
                    findings.Add(ValidationCodes.DriveValue, key, problem);
            }
        }

        return findings.Messages.Count > 0 ? findings.Messages : Check(name, DriveSection.ReadAccount(name, account));
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a name the engine accepts for an account (and for its
    /// token file): what a form checks before it writes an account under it.
    /// </summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>
    /// Whether <paramref name="pattern"/> is an entry <c>Share:AllowedRecipients</c> understands: an
    /// address alone, <c>*@domain</c>, or <c>*</c> for anyone — read as the engine's
    /// <c>RecipientPatterns</c> reads it. The engine trims an entry before it reads it, and so does a caller.
    /// </summary>
    public static bool IsRecipientPattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern == "*")
            return true;

        if (pattern.StartsWith("*@", StringComparison.Ordinal))
        {
            var domain = pattern[2..];
            return domain.Length > 0 && !domain.Contains('@', StringComparison.Ordinal) && !domain.Contains('*', StringComparison.Ordinal);
        }

        return !pattern.Contains('*', StringComparison.Ordinal)
            && MailAddress.TryCreate(pattern, out var address)
            && string.Equals(address.Address, pattern, StringComparison.OrdinalIgnoreCase)
            && pattern.Contains('@', StringComparison.Ordinal);
    }

    /// <summary>
    /// The member of <see cref="DriveSection.Providers"/> the engine binds <paramref name="text"/>
    /// to — a name without regard to case, or its number —, or null when it binds none.
    /// </summary>
    public static string? ProviderOf(string? text) => EmailAccountRules.Member(DriveSection.Providers, text);

    /// <summary>
    /// The engine's sentence on <c>Screening:WithholdRejected</c> when it is neither true nor
    /// false — every account is then set aside on it —, or null.
    /// </summary>
    internal static string? ScreeningProblem(AppSettingsDocument document)
    {
        var text = EmailAccountRules.ConfigurationText(document.Drive.WithholdRejectedNode);
        return string.IsNullOrEmpty(text) || bool.TryParse(text, out _)
            ? null
            : $"{WithholdRejectedPath} '{text}' is neither true nor false";
    }

    /// <summary>The engine's resolver, rule by rule and in its order.</summary>
    private static void Resolve(string name, DriveAccountDefinition definition, Findings findings)
    {
        if (!IsValidName(name))
        {
            findings.Add(ValidationCodes.DriveName, string.Empty,
                "the account name may only hold letters, digits, '.', '_' and '-', starts with a letter or a digit, and has 64 characters at most");
        }

        var provider = ProviderOf(definition.Provider);
        if (provider is null)
            findings.Add(ValidationCodes.DriveProvider, DriveSection.Keys.Provider, "Provider is required: GoogleDrive or OneDrive");

        if ((definition.Rights & DriveSection.AllRights) == DriveRights.None)
        {
            findings.Add(ValidationCodes.DriveRights, DriveSection.Keys.Rights,
                "Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Write, Organize\"");
        }

        ResolveRoot(definition.Root, findings);
        if (provider is not null)
            ResolveOAuth(provider, definition, findings);

        ResolveShare(definition, findings);

        if ((definition.MaxUploadMegabytes ?? DefaultMaxUploadMegabytes) is <= 0 or > MaxUploadMegabytesCeiling)
        {
            findings.Add(ValidationCodes.DriveLimits, DriveSection.Keys.MaxUploadMegabytes,
                string.Create(CultureInfo.InvariantCulture, $"MaxUploadMegabytes must lie between 1 and {MaxUploadMegabytesCeiling}"));
        }

        if (definition.TimeoutSeconds is <= 0)
            findings.Add(ValidationCodes.DriveLimits, DriveSection.Keys.TimeoutSeconds, "TimeoutSeconds must be positive");

        // Studio's own, and never the value: it may be the secret, pasted in the field that names it.
        if (SettingPlaceholder.TryMatch(definition.ClientSecretEnvVar, out _))
        {
            findings.Add(ValidationCodes.DriveAuth, ClientSecretEnvVarKey,
                $"{ClientSecretEnvVarKey} holds a variable reference, and Orkeon expands no variable here: write the NAME of the environment variable alone");
        }
        else if (!LlmSection.IsVariableName(definition.ClientSecretEnvVar))
        {
            findings.Add(ValidationCodes.DriveAuth, ClientSecretEnvVarKey,
                $"{ClientSecretEnvVarKey} must be the name of an environment variable (no '=', space or line break), never the secret itself");
        }
    }

    /// <summary>The first path segment that is no folder name, as the engine's resolver judges it.</summary>
    private static void ResolveRoot(string? root, Findings findings)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        var segments = root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Any(char.IsControl) || segment.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0)
            {
                findings.Add(ValidationCodes.DriveRoot, DriveSection.Keys.Root,
                    $"Root '{root}' is not a folder path from the root of the drive (e.g. \"Orkeon/agents\"): '{segment}' is not a folder name");
                return;
            }
        }
    }

    private static void ResolveOAuth(string provider, DriveAccountDefinition definition, Findings findings)
    {
        if (string.IsNullOrEmpty(definition.ClientId?.Trim()))
        {
            findings.Add(ValidationCodes.DriveAuth, ClientIdKey,
                "Auth:ClientId is required (the id of your Google Cloud or Microsoft Entra application)");
            return;
        }

        if (provider == GoogleDrive)
        {
            if (string.IsNullOrWhiteSpace(definition.ClientSecretEnvVar))
            {
                findings.Add(ValidationCodes.DriveAuth, ClientSecretEnvVarKey,
                    "Auth:ClientSecretEnvVar is required for Google Drive: Google authenticates a desktop client's token requests with its client secret");
            }

            if (!string.IsNullOrWhiteSpace(definition.Tenant))
                findings.Add(ValidationCodes.DriveAuth, TenantKey, "Auth:Tenant is a Microsoft setting; a Google Drive account has none");
            return;
        }

        var tenant = TenantOf(definition);
        if (!tenant.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
            findings.Add(ValidationCodes.DriveAuth, TenantKey, $"Auth:Tenant '{tenant}' is not a tenant alias or id");
    }

    private static void ResolveShare(DriveAccountDefinition definition, Findings findings)
    {
        foreach (var pattern in definition.AllowedRecipients
                     .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                     .Select(pattern => pattern.Trim())
                     .Where(pattern => !IsRecipientPattern(pattern)))
        {
            findings.Add(ValidationCodes.DriveShare, AllowedRecipientsKey,
                $"Share:AllowedRecipients entry '{pattern}' is neither an address, '*@domain' nor '*'");
        }
    }

    /// <summary>The tenant a OneDrive account declares, trimmed, else the default one.</summary>
    internal static string TenantOf(DriveAccountDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Tenant) ? DefaultTenant : definition.Tenant.Trim();

    /// <summary>
    /// The keys of <paramref name="section"/> the engine knows no setting for, at any depth, in the
    /// order the configuration lists them (by name).
    /// </summary>
    private static void UnknownKeys(JsonObject section, string[] known, string prefix, Findings findings)
    {
        foreach (var (key, value) in section.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = prefix + key;
            if (!known.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                var owner = prefix.Length == 0 ? "an account" : prefix.TrimEnd(':');
                findings.Add(ValidationCodes.DriveKey, path, $"{path} is not an account setting: {owner} carries {string.Join(", ", known)}");
            }
            else if (value is JsonObject child && ObjectKeys.TryGetValue(key, out var childKeys))
            {
                UnknownKeys(child, childKeys, path + ":", findings);
            }
        }
    }

    /// <summary>
    /// The binder's sentence on a typed setting it cannot convert, or null when it reads
    /// <paramref name="text"/> — or when the key is absent, or optional and empty.
    /// </summary>
    private static string? Unreadable(string key, Kind kind, bool optional, string? text)
    {
        if (text is null || (optional && text.Length == 0))
            return null;

        return kind switch
        {
            Kind.Integer => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? null : $"{key} '{text}' is not a whole number",
            Kind.Boolean => bool.TryParse(text, out _) ? null : $"{key} '{text}' is neither true nor false",
            Kind.Rights => EmailAccountRules.EnumerationValue(DriveSection.RightNames, text, flags: true) is { } value && (value & ~(long)DriveSection.AllRights) == 0
                ? null
                : $"{key} '{text}' is not a list of {string.Join(", ", DriveSection.RightNames)}, separated by commas",
            _ => ProviderOf(text) is not null ? null : $"{key} '{text}' is not one of {string.Join(", ", DriveSection.Providers)}",
        };
    }

    /// <summary>The text a typed setting holds in <paramref name="definition"/>; a number or a switch always reads.</summary>
    private static string? WrittenText(DriveAccountDefinition definition, string key) => key switch
    {
        DriveSection.Keys.Provider => definition.Provider,
        DriveSection.Keys.Rights => (definition.Rights & DriveSection.AllRights) == DriveRights.None ? definition.RightsRaw : null,
        _ => null,
    };

    /// <summary>The findings of one account, each a warning at a key under the account's path.</summary>
    private sealed class Findings
    {
        private readonly string _path;

        public Findings(string name)
        {
            _path = $"{DriveSection.AccountsPath}:{name}";
            Messages = [];
        }

        public List<ValidationMessage> Messages { get; }

        public void Add(string code, string key, string text) =>
            Messages.Add(ValidationMessage.Warning(code, text, key.Length == 0 ? _path : $"{_path}:{key}"));
    }
}

/// <summary>Keys equal but for the case in one object of <c>Orkeon:Tools:Drive</c>; see <see cref="TwinKeys"/>.</summary>
internal static class DriveTwinKeys
{
    /// <summary>The keys Studio knows in the section, whatever the object that carries them, as it spells them.</summary>
    private static readonly string[] Spellings =
    [
        DriveSection.Keys.DefaultAccount, DriveSection.Keys.CredentialsDirectory, DriveSection.Keys.Accounts,
        DriveSection.Keys.Screening, DriveSection.Keys.WithholdRejected,
        .. DriveAccountRules.AccountKeys, .. DriveAccountRules.ObjectKeys.Values.SelectMany(keys => keys),
    ];

    private static readonly TwinKeys Check = new(Spellings, ValidationCodes.DriveTwin);

    /// <summary>
    /// The twin keys of <paramref name="owner"/> (at <paramref name="path"/>) and of every object
    /// under it; see <see cref="TwinKeys.Under"/>.
    /// </summary>
    public static IEnumerable<ValidationMessage> Under(JsonObject owner, string path, JsonObject? accounts = null) =>
        Check.Under(owner, path, accounts);
}
