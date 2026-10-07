using System.Text.Json.Nodes;
using Orkeon.Constants.Configuration;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over <c>Orkeon:Tools:Email</c> (STUDIO-65): the accounts, keyed by name under
/// <c>Accounts</c>, and the three settings that apply to all of them. Every write lands in place,
/// key by key, so a key Studio does not model survives an edit of the account that carries it,
/// and the engine reads the result exactly as written: nothing here writes an empty string, an
/// empty object or an empty list, because the engine reads each of them as a value (an empty
/// <c>Host</c> blocks the provider preset, an empty <c>Rights</c> sets the account aside). A
/// field cleared removes its key, and an object emptied removes itself.
/// </summary>
/// <remarks>
/// Every key inside the section is looked up without regard to case, as the configuration reads
/// it, and the spelling found in the file is kept: a value is written to, or removed from, the
/// key the file holds — never to a twin beside it, which no run could read —, and only a key
/// the file lacks is created, in the spelling of <see cref="Keys"/>. Account names are compared
/// the same way, as the engine's dictionary does. The path down to the section
/// (<c>Orkeon:Tools:Email</c>) is read as written, like every other section. The names of the keys and of the enumeration values are
/// copies of what <c>Orkeon.Tools.Email</c> binds — Core does not reference it, so as not to carry
/// MailKit into the forms — and a test holds each of them to the engine's binder.
/// </remarks>
public sealed class EmailSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = ConfigurationKeys.ToolsEmail;

    /// <summary>Configuration path of the accounts dictionary.</summary>
    public const string AccountsPath = SectionPath + ":" + Keys.Accounts;

    /// <summary>Every right the engine declares, together: a bit outside them is no right.</summary>
    internal static readonly EmailRight AllRights = Enum.GetValues<EmailRight>().Aggregate(EmailRight.None, (all, right) => all | right);

    private readonly AppSettingsDocument _document;

    internal EmailSection(AppSettingsDocument document) => _document = document;

    /// <summary>The <c>Provider</c> values the engine knows, in the spelling it binds.</summary>
    public static IReadOnlyList<string> Providers { get; } = [Values.Custom, Values.Gmail, Values.Outlook];

    /// <summary>The <c>Incoming:Protocol</c> values the engine knows.</summary>
    public static IReadOnlyList<string> IncomingProtocols { get; } = [Values.Imap, Values.Pop3, Values.Graph];

    /// <summary>The <c>Outgoing:Protocol</c> values the engine knows.</summary>
    public static IReadOnlyList<string> OutgoingProtocols { get; } = [Values.Smtp, Values.Graph];

    /// <summary>The <c>Security</c> values the engine knows, for the incoming and the outgoing side alike.</summary>
    public static IReadOnlyList<string> Securities { get; } = [Values.SslOnConnect, Values.StartTls, Values.NoSecurity];

    /// <summary>The <c>Auth:Method</c> values the engine knows.</summary>
    public static IReadOnlyList<string> AuthMethods { get; } = [Values.Password, Values.OAuth2];

    /// <summary>The names of the rights, in the order the engine lists them and Studio writes them.</summary>
    public static IReadOnlyList<string> Rights { get; } = ["Read", "Organize", "Draft", "Send", "Delete", "Purge"];

    /// <summary>The account names, as the file spells them, in document order.</summary>
    public IReadOnlyList<string> AccountNames => AccountsNode is { } accounts ? [.. accounts.Select(property => property.Key)] : [];

    /// <summary>The account a call uses when it names none; absent means the only account.</summary>
    public string? DefaultAccount
    {
        get => ReadText(Section, Keys.DefaultAccount);
        set => WriteSectionText(Keys.DefaultAccount, value);
    }

    /// <summary>
    /// Whether a message the prompt-injection detector rejects has its body withheld
    /// (<c>Screening:WithholdRejected</c>); absent means <see langword="false"/> to the engine.
    /// Clearing it removes <c>Screening</c> too, unless a key Studio does not model lives there.
    /// </summary>
    public bool? WithholdRejected
    {
        get => AppSettingsDocument.ReadBoolean(WithholdRejectedNode);
        set
        {
            // Clearing never creates the section, and leaves alone what is no object under Screening.
            if (value is null && Node(Section, Keys.Screening) is not JsonObject)
                return;

            WriteObject(EnsureSection(), Keys.Screening, screening =>
            {
                if (value is null)
                    Remove(screening, Keys.WithholdRejected);
                else
                    Write(screening, Keys.WithholdRejected, JsonValue.Create(value.Value));
            });
        }
    }

    /// <summary>
    /// The physical directory that holds the OAuth tokens, for a host whose per-user settings
    /// directory is not the right one. A physical path, not a virtual one: no mount applies.
    /// </summary>
    public string? CredentialsDirectory
    {
        get => ReadText(Section, Keys.CredentialsDirectory);
        set => WriteSectionText(Keys.CredentialsDirectory, value);
    }

    /// <summary>
    /// The account under <paramref name="name"/> (the exact spelling first, then without regard
    /// to case), or null when the file holds no such name. <see cref="EmailAccountDefinition.Name"/>
    /// carries the spelling the file holds. A value that is no object (<c>"work": null</c>) is an
    /// account all the same, listed by <see cref="AccountNames"/> and by the engine: it reads as
    /// one with no field.
    /// </summary>
    public EmailAccountDefinition? GetAccount(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (AccountsNode is not { } accounts || FindKey(accounts, name) is not { } key)
            return null;

        return ReadAccount(key, accounts[key] as JsonObject);
    }

    /// <summary>
    /// The account <paramref name="account"/> holds under <paramref name="key"/>, whatever the key —
    /// a blank one included, which the engine lists too. A node that is no object (STUDIO-66: the
    /// engine then reads an account that declares nothing) reads as an account with no field.
    /// </summary>
    internal static EmailAccountDefinition ReadAccount(string key, JsonObject? account)
    {
        var incoming = Node(account, Keys.Incoming) as JsonObject;
        var outgoing = Node(account, Keys.Outgoing) as JsonObject;
        var auth = Node(account, Keys.Auth) as JsonObject;
        var send = Node(account, Keys.Send) as JsonObject;
        var rights = ReadText(account, Keys.Rights);
        var parsed = TryParseRights(rights, out var granted) ? granted : EmailRight.None;

        return new EmailAccountDefinition
        {
            Name = key,
            Provider = ReadText(account, Keys.Provider),
            Address = ReadText(account, Keys.Address),
            DisplayName = ReadText(account, Keys.DisplayName),
            Rights = parsed,
            RightsRaw = parsed == EmailRight.None && !string.IsNullOrWhiteSpace(rights) ? rights : null,
            TimeoutSeconds = ReadInt(account, Keys.TimeoutSeconds),
            SaveSentCopy = AppSettingsDocument.ReadBoolean(Node(account, Keys.SaveSentCopy)),
            IncomingProtocol = ReadText(incoming, Keys.Protocol),
            IncomingHost = ReadText(incoming, Keys.Host),
            IncomingPort = ReadInt(incoming, Keys.Port),
            IncomingSecurity = ReadText(incoming, Keys.Security),
            OutgoingProtocol = ReadText(outgoing, Keys.Protocol),
            OutgoingHost = ReadText(outgoing, Keys.Host),
            OutgoingPort = ReadInt(outgoing, Keys.Port),
            OutgoingSecurity = ReadText(outgoing, Keys.Security),
            AuthMethod = ReadText(auth, Keys.Method),
            Username = ReadText(auth, Keys.Username),
            PasswordEnvVar = ReadText(auth, Keys.PasswordEnvVar),
            ClientId = ReadText(auth, Keys.ClientId),
            ClientSecretEnvVar = ReadText(auth, Keys.ClientSecretEnvVar),
            Tenant = ReadText(auth, Keys.Tenant),
            AllowedRecipients = [.. ReadStrings(Node(send, Keys.AllowedRecipients))],
            MaxRecipients = ReadInt(send, Keys.MaxRecipients),
            MaxPerHour = ReadInt(send, Keys.MaxPerHour),
        };
    }

    /// <summary>
    /// Writes the account in place under its name — a new one goes last — and elects no default:
    /// a field that is null or blank, an empty recipient list and an object left empty remove
    /// their keys. A value the file already holds in another spelling that reads the same
    /// (<c>"read,SEND"</c>, <c>"993"</c>) is left as it is, so saving an account the user did not
    /// change changes nothing — a number or a switch the engine cannot read included, which reads
    /// as null and which a null leaves where it was, and so do a value that is no object under
    /// <c>Incoming</c>, <c>Outgoing</c>, <c>Auth</c> or <c>Send</c> while nothing is set under it,
    /// and a recipient list written back as it was read. The account object itself stays, however
    /// empty, so that it is still listed; a value that is no object (<c>"work": null</c>) stays
    /// as written while the account brings no field, and gives way to the account's object as
    /// soon as it brings one.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The name is blank, or differs from an existing account's only by case: the engine could not
    /// tell the two apart, so this is the same account and a rename is the way to respell it.
    /// </exception>
    public void SetAccount(EmailAccountDefinition account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Name);

        var accounts = EnsureAccounts();
        var key = FindKey(accounts, account.Name);
        if (key is not null && !string.Equals(key, account.Name, StringComparison.Ordinal))
            throw new ArgumentException($"'{account.Name}' is the account '{key}' with another case: the engine reads them as one.", nameof(account));

        key ??= account.Name;
        var declared = accounts.ContainsKey(key);
        var existing = accounts[key] as JsonObject;
        var target = existing ?? [];

        WriteText(target, Keys.Provider, account.Provider);
        WriteText(target, Keys.Address, account.Address);
        WriteText(target, Keys.DisplayName, account.DisplayName);
        WriteRights(target, account);
        WriteObject(target, Keys.Incoming, incoming =>
        {
            WriteText(incoming, Keys.Protocol, account.IncomingProtocol);
            WriteText(incoming, Keys.Host, account.IncomingHost);
            WriteInt(incoming, Keys.Port, account.IncomingPort);
            WriteText(incoming, Keys.Security, account.IncomingSecurity);
        });
        WriteObject(target, Keys.Outgoing, outgoing =>
        {
            WriteText(outgoing, Keys.Protocol, account.OutgoingProtocol);
            WriteText(outgoing, Keys.Host, account.OutgoingHost);
            WriteInt(outgoing, Keys.Port, account.OutgoingPort);
            WriteText(outgoing, Keys.Security, account.OutgoingSecurity);
        });
        WriteObject(target, Keys.Auth, auth =>
        {
            WriteText(auth, Keys.Method, account.AuthMethod);
            WriteText(auth, Keys.Username, account.Username);
            WriteText(auth, Keys.PasswordEnvVar, account.PasswordEnvVar);
            WriteText(auth, Keys.ClientId, account.ClientId);
            WriteText(auth, Keys.ClientSecretEnvVar, account.ClientSecretEnvVar);
            WriteText(auth, Keys.Tenant, account.Tenant);
        });
        WriteObject(target, Keys.Send, send =>
        {
            WriteRecipients(send, account.AllowedRecipients);
            WriteInt(send, Keys.MaxRecipients, account.MaxRecipients);
            WriteInt(send, Keys.MaxPerHour, account.MaxPerHour);
        });
        WriteInt(target, Keys.TimeoutSeconds, account.TimeoutSeconds);
        WriteBoolean(target, Keys.SaveSentCopy, account.SaveSentCopy);

        // What is no object under the name read as an account with no field: written back with
        // none, it is that same value.
        if (existing is null && (target.Count > 0 || !declared))
            accounts[key] = target;
    }

    /// <summary>
    /// Removes the account and everything under it; the dictionary goes when it empties, and so
    /// does <see cref="DefaultAccount"/> when it named the account and no other account answers
    /// to that name.
    /// </summary>
    public void RemoveAccount(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (AccountsNode is not { } accounts || FindKey(accounts, name) is not { } key)
            return;

        accounts.Remove(key);
        if (DefaultAccount is { } named
            && string.Equals(named, key, StringComparison.OrdinalIgnoreCase)
            && FindKey(accounts, named) is null)
        {
            DefaultAccount = null;
        }

        if (accounts.Count == 0)
            Remove(Section, Keys.Accounts);
    }

    /// <summary>
    /// Moves an account under a new name, every key it carries included, and keeps it where it
    /// stood in the document. <see cref="DefaultAccount"/> follows when it named the account.
    /// Respelling a name in another case is a rename of the same account, not a second one.
    /// </summary>
    /// <exception cref="ArgumentException">The new name is blank, or another account already answers to it.</exception>
    public void RenameAccount(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        if (AccountsNode is not { } accounts || FindKey(accounts, oldName) is not { } key)
            return;
        if (string.Equals(key, newName, StringComparison.Ordinal))
            return;
        if (accounts.Any(property => !string.Equals(property.Key, key, StringComparison.Ordinal)
                                     && string.Equals(property.Key, newName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"An account already answers to '{newName}' (names are compared without regard to case).", nameof(newName));
        }

        // JsonObject has no in-place rename: lift every property out and put them back in order.
        var entries = accounts.Select(property => (property.Key, Value: property.Value?.DeepClone())).ToList();
        accounts.Clear();
        foreach (var (entryKey, value) in entries)
            accounts[string.Equals(entryKey, key, StringComparison.Ordinal) ? newName : entryKey] = value;

        if (DefaultAccount is { } named && string.Equals(named, key, StringComparison.OrdinalIgnoreCase))
            DefaultAccount = newName;
    }

    /// <summary>The section's object, or null when the file holds none.</summary>
    private JsonObject? Section => _document.GetNode(SectionPath) as JsonObject;

    /// <summary>The accounts dictionary as the file holds it, whatever the case of its key; null when there is none.</summary>
    internal JsonObject? AccountsNode => Node(Section, Keys.Accounts) as JsonObject;

    /// <summary>What stands under <c>Screening:WithholdRejected</c>, whatever the case of the two keys.</summary>
    internal JsonNode? WithholdRejectedNode => Node(Node(Section, Keys.Screening) as JsonObject, Keys.WithholdRejected);

    private JsonObject EnsureSection()
    {
        if (Section is { } section)
            return section;

        section = [];
        _document.SetNode(SectionPath, section);
        return section;
    }

    private JsonObject EnsureAccounts()
    {
        if (AccountsNode is { } accounts)
            return accounts;

        var section = EnsureSection();
        accounts = [];
        section[FindKey(section, Keys.Accounts) ?? Keys.Accounts] = accounts;
        return accounts;
    }

    /// <summary>Writes a text of the section itself; clearing it never creates the section.</summary>
    private void WriteSectionText(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Remove(Section, key);
        else
            Write(EnsureSection(), key, JsonValue.Create(value));
    }

    /// <summary>
    /// The key of <paramref name="owner"/> spelt exactly as <paramref name="name"/>, else the first
    /// that differs only by case — the one the configuration reads under that name.
    /// </summary>
    private static string? FindKey(JsonObject owner, string name) =>
        owner.ContainsKey(name)
            ? name
            : owner.Select(property => property.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What <paramref name="owner"/> holds under <paramref name="key"/>, whatever the case the file spells it in.</summary>
    private static JsonNode? Node(JsonObject? owner, string key) =>
        owner is not null && FindKey(owner, key) is { } found ? owner[found] : null;

    /// <summary>Removes the key the file holds for <paramref name="key"/>, whatever its case.</summary>
    private static void Remove(JsonObject? owner, string key)
    {
        if (owner is not null && FindKey(owner, key) is { } found)
            owner.Remove(found);
    }

    /// <summary>Writes under the key the file holds for <paramref name="key"/>, or creates it as <see cref="Keys"/> spells it.</summary>
    private static void Write(JsonObject owner, string key, JsonNode value) =>
        owner[FindKey(owner, key) ?? key] = value;

    // Reads: the document's own readers over a node in hand — an account name may hold a colon or a
    // blank, which a colon-separated path could not spell.

    private static string? ReadText(JsonObject? owner, string key) => AppSettingsDocument.ReadString(Node(owner, key));

    private static int? ReadInt(JsonObject? owner, string key) => AppSettingsDocument.ReadInt32(Node(owner, key));

    private static IEnumerable<string> ReadStrings(JsonNode? node) =>
        node is JsonArray array
            ? array.OfType<JsonValue>().Select(value => AppSettingsDocument.ReadString(value)).OfType<string>()
            : [];

    /// <summary>
    /// Reads the rights the way the binder's converter does (names without regard to case, or a
    /// number), and refuses what it would refuse: a number with an undefined bit, and nothing granted.
    /// </summary>
    private static bool TryParseRights(string? text, out EmailRight rights)
    {
        rights = EmailRight.None;
        if (text is null
            || !Enum.TryParse(text, ignoreCase: true, out EmailRight parsed)
            || parsed == EmailRight.None
            || (parsed & ~AllRights) != EmailRight.None)
        {
            return false;
        }

        rights = parsed;
        return true;
    }

    // Writes: a value that reads the same as the one in the file leaves the node alone.

    private static void WriteText(JsonObject owner, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Remove(owner, key);
        else if (!string.Equals(ReadText(owner, key), value, StringComparison.Ordinal))
            Write(owner, key, JsonValue.Create(value));
    }

    private static void WriteInt(JsonObject owner, string key, int? value)
    {
        if (value is null)
            Clear(owner, key, readable: ReadInt(owner, key) is not null);
        else if (ReadInt(owner, key) != value)
            Write(owner, key, JsonValue.Create(value.Value));
    }

    private static void WriteBoolean(JsonObject owner, string key, bool? value)
    {
        if (value is null)
            Clear(owner, key, readable: AppSettingsDocument.ReadBoolean(Node(owner, key)) is not null);
        else if (AppSettingsDocument.ReadBoolean(Node(owner, key)) != value)
            Write(owner, key, JsonValue.Create(value.Value));
    }

    /// <summary>
    /// Clears a number or a switch: the key goes when it held a value that reads, or nothing (a
    /// JSON null, a blank text). A value the engine cannot read (<c>"Port": "abc"</c>) came back
    /// as null when the account was read, so a null written back is that same value, unchanged:
    /// it stays as the file spelt it, with the finding the run makes of it, until a value that
    /// reads replaces it.
    /// </summary>
    private static void Clear(JsonObject owner, string key, bool readable)
    {
        if (readable
            || Node(owner, key) is not { } written
            || (written is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)))
        {
            Remove(owner, key);
        }
    }

    /// <summary>
    /// The rights as the engine reads them, <c>"Read, Organize, Draft"</c>; when none is granted
    /// the text the file held (<see cref="EmailAccountDefinition.RightsRaw"/>) is written back as it was.
    /// </summary>
    private static void WriteRights(JsonObject owner, EmailAccountDefinition account)
    {
        var granted = account.Rights & AllRights;
        if (granted == EmailRight.None)
        {
            WriteText(owner, Keys.Rights, account.RightsRaw);
            return;
        }

        if (TryParseRights(ReadText(owner, Keys.Rights), out var written) && written == granted)
            return;

        var names = Enum.GetValues<EmailRight>().Where(right => right != EmailRight.None && granted.HasFlag(right));
        Write(owner, Keys.Rights, JsonValue.Create(string.Join(", ", names)));
    }

    /// <summary>
    /// Writes the list, unless it is the one the file reads as: what is no text in the array
    /// (<c>["a@x.org", 42]</c>), and what is no array at all, never came into the list that was
    /// read, so the same list written back leaves the node as the file spelt it.
    /// </summary>
    private static void WriteRecipients(JsonObject owner, IEnumerable<string> recipients)
    {
        var kept = recipients.Where(recipient => !string.IsNullOrWhiteSpace(recipient)).ToList();
        if (kept.SequenceEqual(ReadStrings(Node(owner, Keys.AllowedRecipients)), StringComparer.Ordinal))
            return;

        if (kept.Count == 0)
        {
            Remove(owner, Keys.AllowedRecipients);
            return;
        }

        var array = new JsonArray();
        foreach (var recipient in kept)
            array.Add(JsonValue.Create(recipient));

        Write(owner, Keys.AllowedRecipients, array);
    }

    /// <summary>
    /// Fills the object under <paramref name="key"/>, creating it when needed and removing it when
    /// it ends up empty. What is no object under the key (<c>"Auth": "oauth"</c>) read as no field:
    /// it stays as written while nothing is set under it, and gives way to the object as soon as
    /// something is.
    /// </summary>
    private static void WriteObject(JsonObject owner, string key, Action<JsonObject> fill)
    {
        var existing = Node(owner, key) as JsonObject;
        var child = existing ?? [];

        fill(child);

        if (existing is null)
        {
            if (child.Count > 0)
                Write(owner, key, child);
        }
        else if (child.Count == 0)
        {
            Remove(owner, key);
        }
    }

    /// <summary>
    /// The enumeration values the engine binds, one constant each, for the code that branches on
    /// one: the rules, the effective values and the form. The lists above are made of them and of
    /// nothing else, and a test holds each list to the engine's enumeration.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034",
        Justification = "The values read beside the keys they are written under (EmailSection.Values.Gmail); " +
                        "a top-level class would detach them from the lists a test holds to the engine.")]
    public static class Values
    {
        /// <summary>Provider: no preset — the hosts are given.</summary>
        public const string Custom = "Custom";

        /// <summary>Provider: the Gmail preset.</summary>
        public const string Gmail = "Gmail";

        /// <summary>Provider: the Outlook.com / Microsoft 365 preset.</summary>
        public const string Outlook = "Outlook";

        /// <summary>Incoming protocol: IMAP.</summary>
        public const string Imap = "Imap";

        /// <summary>Incoming protocol: POP3.</summary>
        public const string Pop3 = "Pop3";

        /// <summary>Incoming and outgoing protocol: Microsoft Graph.</summary>
        public const string Graph = "Graph";

        /// <summary>Outgoing protocol: SMTP.</summary>
        public const string Smtp = "Smtp";

        /// <summary>Security: TLS from the first byte.</summary>
        public const string SslOnConnect = "SslOnConnect";

        /// <summary>Security: a clear connection upgraded to TLS.</summary>
        public const string StartTls = "StartTls";

        /// <summary>Security: none — the engine allows it towards the loopback only.</summary>
        public const string NoSecurity = "None";

        /// <summary>Auth method: a password read from a variable.</summary>
        public const string Password = "Password";

        /// <summary>Auth method: OAuth2, signed in with <c>orkeon email login</c>.</summary>
        public const string OAuth2 = "OAuth2";
    }

    /// <summary>
    /// The names of the keys <c>Orkeon:Tools:Email</c> binds, one constant each. Core does not
    /// reference the engine, so a test holds this list to the options classes the binder fills.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034",
        Justification = "The key names read as the path they spell (EmailSection.Keys.Incoming); a top-level " +
                        "class would detach them from the section they belong to and from the plan's naming.")]
    public static class Keys
    {
        /// <summary>Section: the account a call uses when it names none.</summary>
        public const string DefaultAccount = "DefaultAccount";

        /// <summary>Section: the physical directory of the OAuth tokens.</summary>
        public const string CredentialsDirectory = "CredentialsDirectory";

        /// <summary>Section: the accounts dictionary.</summary>
        public const string Accounts = "Accounts";

        /// <summary>Section: how received content is screened.</summary>
        public const string Screening = "Screening";

        /// <summary>Screening: whether a rejected message has its body withheld.</summary>
        public const string WithholdRejected = "WithholdRejected";

        /// <summary>Account: the provider preset.</summary>
        public const string Provider = "Provider";

        /// <summary>Account: the address.</summary>
        public const string Address = "Address";

        /// <summary>Account: the display name.</summary>
        public const string DisplayName = "DisplayName";

        /// <summary>Account: what an agent may do.</summary>
        public const string Rights = "Rights";

        /// <summary>Account: the reading side.</summary>
        public const string Incoming = "Incoming";

        /// <summary>Account: the sending side.</summary>
        public const string Outgoing = "Outgoing";

        /// <summary>Account: how it authenticates.</summary>
        public const string Auth = "Auth";

        /// <summary>Account: the guard rails on sending.</summary>
        public const string Send = "Send";

        /// <summary>Account: the protocol timeout.</summary>
        public const string TimeoutSeconds = "TimeoutSeconds";

        /// <summary>Account: whether a sent message is appended to the Sent folder.</summary>
        public const string SaveSentCopy = "SaveSentCopy";

        /// <summary>Incoming and Outgoing: the protocol.</summary>
        public const string Protocol = "Protocol";

        /// <summary>Incoming and Outgoing: the server host.</summary>
        public const string Host = "Host";

        /// <summary>Incoming and Outgoing: the server port.</summary>
        public const string Port = "Port";

        /// <summary>Incoming and Outgoing: the transport security.</summary>
        public const string Security = "Security";

        /// <summary>Auth: the method.</summary>
        public const string Method = "Method";

        /// <summary>Auth: the login name.</summary>
        public const string Username = "Username";

        /// <summary>Auth: the name of the variable that holds the password.</summary>
        public const string PasswordEnvVar = "PasswordEnvVar";

        /// <summary>Auth: the OAuth client id.</summary>
        public const string ClientId = "ClientId";

        /// <summary>Auth: the name of the variable that holds the OAuth client secret.</summary>
        public const string ClientSecretEnvVar = "ClientSecretEnvVar";

        /// <summary>Auth: the Microsoft tenant.</summary>
        public const string Tenant = "Tenant";

        /// <summary>Send: who may receive mail from the account.</summary>
        public const string AllowedRecipients = "AllowedRecipients";

        /// <summary>Send: the most recipients one message may have.</summary>
        public const string MaxRecipients = "MaxRecipients";

        /// <summary>Send: the most messages the process sends per hour.</summary>
        public const string MaxPerHour = "MaxPerHour";
    }
}
