using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Validation;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-66 — what Studio says of an e-mail account is what the run will say. Studio Core spells
/// the engine's rules again (<see cref="EmailAccountRules"/>, <see cref="EmailAccountEffective"/>)
/// instead of referencing <c>Orkeon.Tools.Email</c>, so every row here is handed to the engine's
/// own binder, registry and resolver: the account is usable or set aside on both sides, each family
/// of problems counts the same and opens on the same sentence, and a field left blank is worth
/// the same value. A rule the engine gains turns its row red, and the rule is then added in Core.
/// </summary>
public sealed class EmailValidationOracleTests
{
    private const string Name = "work";

    /// <summary>A Gmail account the engine resolves: the base most rows change one field of.</summary>
    private static readonly EmailAccountDefinition Gmail = new()
    {
        Name = Name,
        Provider = "Gmail",
        Address = "me@gmail.com",
        Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft,
        PasswordEnvVar = "GMAIL_APP_PASSWORD",
    };

    /// <summary>An Outlook account the engine resolves: Graph both ways, OAuth2.</summary>
    private static readonly EmailAccountDefinition Outlook = new()
    {
        Name = Name,
        Provider = "Outlook",
        Address = "me@hotmail.com",
        Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft,
        ClientId = "00000000-0000-0000-0000-000000000000",
    };

    /// <summary>A Custom account the engine resolves: its own IMAP and SMTP servers, a password.</summary>
    private static readonly EmailAccountDefinition Custom = new()
    {
        Name = Name,
        Address = "me@example.com",
        Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft,
        IncomingHost = "imap.example.com",
        OutgoingHost = "smtp.example.com",
        PasswordEnvVar = "WORK_MAIL_PASSWORD",
    };

    /// <summary>
    /// The accounts built with <see cref="EmailAccountDefinition"/> and written by
    /// <see cref="EmailSection"/>, each with the codes Studio raises on it, in order.
    /// </summary>
    private static readonly Dictionary<string, (EmailAccountDefinition Account, string[] Codes)> Definitions = new(StringComparer.Ordinal)
    {
        ["gmail with a password, everything else left to the preset"] = (Gmail, []),
        ["gmail read through pop3"] = (Gmail with { IncomingProtocol = "Pop3" }, []),
        ["gmail oauth2 with its client secret"] = (Gmail with { PasswordEnvVar = null, ClientId = "id.apps.googleusercontent.com", ClientSecretEnvVar = "GMAIL_CLIENT_SECRET" }, []),
        ["gmail oauth2 without a client secret"] = (Gmail with { PasswordEnvVar = null, ClientId = "id.apps.googleusercontent.com" }, [ValidationCodes.EmailAuth]),
        ["gmail with a client id and a password method"] = (Gmail with { AuthMethod = "Password", ClientId = "id.apps.googleusercontent.com" }, []),
        ["gmail spelt in lower case"] = (Gmail with { Provider = "gmail", IncomingProtocol = "imap", IncomingSecurity = "starttls" }, []),

        ["outlook, everything left to the preset"] = (Outlook, []),
        ["outlook read through imap"] = (Outlook with { IncomingProtocol = "Imap" }, []),
        ["outlook read through pop3"] = (Outlook with { IncomingProtocol = "Pop3" }, []),
        ["outlook with a password"] = (Outlook with { AuthMethod = "Password", PasswordEnvVar = "OUTLOOK_PASSWORD" }, [ValidationCodes.EmailAuth]),
        ["outlook with a password method and no variable"] = (Outlook with { AuthMethod = "Password" }, [ValidationCodes.EmailAuth, ValidationCodes.EmailAuth]),
        ["outlook without a client id"] = (Outlook with { ClientId = null }, [ValidationCodes.EmailAuth]),
        ["outlook sending through smtp under graph"] = (Outlook with { OutgoingProtocol = "Smtp" }, [ValidationCodes.EmailServer]),
        ["outlook with a tenant domain"] = (Outlook with { Tenant = "contoso.onmicrosoft.com" }, []),
        ["outlook with a tenant alias"] = (Outlook with { Tenant = " organizations " }, []),
        ["outlook with a tenant that is no alias"] = (Outlook with { Tenant = "my tenant" }, [ValidationCodes.EmailAuth]),

        ["custom imap and smtp"] = (Custom, []),
        ["custom without an incoming host"] = (Custom with { IncomingHost = null }, [ValidationCodes.EmailServer]),
        ["custom without an outgoing host"] = (Custom with { OutgoingHost = null }, []),
        ["custom without an outgoing host granted send"] = (Custom with { OutgoingHost = null, Rights = EmailRight.Read | EmailRight.Send }, [ValidationCodes.EmailSend]),
        ["custom read through pop3"] = (Custom with { IncomingProtocol = "Pop3", IncomingSecurity = "StartTls" }, []),
        ["custom with oauth2"] = (Custom with { AuthMethod = "OAuth2", ClientId = "client" }, [ValidationCodes.EmailAuth]),
        ["custom with a client id and no method"] = (Custom with { ClientId = "client" }, [ValidationCodes.EmailAuth]),
        ["custom read through graph"] = (Custom with { IncomingProtocol = "Graph" }, [ValidationCodes.EmailServer]),
        ["custom sending through graph"] = (Custom with { OutgoingProtocol = "Graph", Rights = EmailRight.Read | EmailRight.Send }, [ValidationCodes.EmailServer, ValidationCodes.EmailSend]),
        ["custom with a login name and a display name"] = (Custom with { Username = " me ", DisplayName = "Me" }, []),
        ["custom keeping no sent copy"] = (Custom with { SaveSentCopy = false }, []),

        ["no encryption towards localhost"] = (Custom with { IncomingHost = "localhost", IncomingSecurity = "None", OutgoingHost = "LOCALHOST", OutgoingSecurity = "None", OutgoingPort = 2525 }, []),
        ["no encryption towards a loopback address"] = (Custom with { IncomingHost = "127.0.0.2", IncomingSecurity = "None", OutgoingHost = "[::1]", OutgoingSecurity = "None" }, []),
        ["no encryption towards a distant host"] = (Custom with { IncomingSecurity = "None", OutgoingSecurity = "None" }, [ValidationCodes.EmailServer, ValidationCodes.EmailServer]),
        ["no encryption towards the preset host"] = (Gmail with { IncomingSecurity = "None" }, [ValidationCodes.EmailServer]),

        ["no rights"] = (Gmail with { Rights = EmailRight.None }, [ValidationCodes.EmailRights]),
        ["rights written none"] = (Gmail with { Rights = EmailRight.None, RightsRaw = "None" }, [ValidationCodes.EmailRights]),
        ["rights the engine cannot read"] = (Gmail with { Rights = EmailRight.None, RightsRaw = "Read, Reed" }, [ValidationCodes.EmailValue]),
        ["no address"] = (Gmail with { Address = null }, [ValidationCodes.EmailAddress]),
        ["an address without an at sign"] = (Gmail with { Address = "me.gmail.com" }, [ValidationCodes.EmailAddress]),
        ["an address without a domain"] = (Gmail with { Address = "me@" }, [ValidationCodes.EmailAddress]),
        ["two addresses"] = (Gmail with { Address = "me@gmail.com, you@gmail.com" }, [ValidationCodes.EmailAddress]),
        ["an address with its display name"] = (Gmail with { Address = "Nom <a@b>" }, []),
        ["an address between blanks"] = (Gmail with { Address = "  me@gmail.com " }, []),

        ["an incoming port of zero"] = (Custom with { IncomingPort = 0 }, [ValidationCodes.EmailServer]),
        ["an outgoing port above the last one"] = (Custom with { OutgoingPort = 65536 }, [ValidationCodes.EmailServer]),
        ["the first and the last port"] = (Custom with { IncomingPort = 1, OutgoingPort = 65535 }, []),
        ["a timeout of zero"] = (Gmail with { TimeoutSeconds = 0 }, [ValidationCodes.EmailServer]),
        ["a timeout"] = (Gmail with { TimeoutSeconds = 30 }, []),
        ["no recipient at most"] = (Gmail with { MaxRecipients = 0 }, [ValidationCodes.EmailSend]),
        ["no message per hour"] = (Gmail with { MaxPerHour = 0 }, [ValidationCodes.EmailSend]),
        ["recipients the engine understands"] = (Gmail with { Rights = EmailRight.Read | EmailRight.Send, AllowedRecipients = ["a@b.c", "*@b.c", "*", " A@B.C "], MaxRecipients = 5, MaxPerHour = 20 }, []),
        ["a recipient domain without its at sign"] = (Gmail with { AllowedRecipients = ["*.b.c"] }, [ValidationCodes.EmailSend]),
        ["a recipient without a local part"] = (Gmail with { AllowedRecipients = ["@b.c"] }, [ValidationCodes.EmailSend]),
        ["two recipients the engine refuses"] = (Gmail with { AllowedRecipients = ["a@b.c", "Bob <a@b.c>", "*@"] }, [ValidationCodes.EmailSend, ValidationCodes.EmailSend]),

        ["a name starting with a dash"] = (Gmail with { Name = "-a" }, [ValidationCodes.EmailName]),
        ["a name of 65 characters"] = (Gmail with { Name = new string('a', 65) }, [ValidationCodes.EmailName]),
        ["a name of 64 characters"] = (Gmail with { Name = new string('a', 64) }, []),
        ["a name with a blank"] = (Gmail with { Name = "a b" }, [ValidationCodes.EmailName]),
        ["a name with every character allowed"] = (Gmail with { Name = "4.b_c-D" }, []),

        // GAP-55 will make the engine refuse a ${NAME} reference: this row then turns red, and the
        // rule is added to EmailAccountRules.
        ["a password variable written as a reference"] = (Gmail with { PasswordEnvVar = "${GMAIL_APP_PASSWORD}" }, []),

        ["a provider the engine does not know"] = (Gmail with { Provider = "Yahoo" }, [ValidationCodes.EmailValue]),
        ["a security the engine does not know"] = (Gmail with { IncomingSecurity = "Tls", OutgoingProtocol = "Imap" }, [ValidationCodes.EmailValue, ValidationCodes.EmailValue]),
        ["a method the engine does not know"] = (Gmail with { AuthMethod = "Kerberos" }, [ValidationCodes.EmailValue]),
        ["an account that declares nothing"] = (new EmailAccountDefinition { Name = Name },
            [ValidationCodes.EmailAddress, ValidationCodes.EmailRights, ValidationCodes.EmailServer, ValidationCodes.EmailAuth]),
    };

    /// <summary>
    /// The accounts written by hand, as <c>Orkeon:Tools:Email</c> sections holding the account
    /// <c>work</c>: what a file may carry that <see cref="EmailSection"/> never writes. The file
    /// is judged as it is, not as Studio would write it again.
    /// </summary>
    private static readonly Dictionary<string, (string Section, string[] Codes)> Files = new(StringComparer.Ordinal)
    {
        ["an empty provider"] = (Account("""{ "Provider": "", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["empty rights"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["blank rights"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "  ", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["an empty incoming host blocks the preset"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Host": "" }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailServer]),
        ["an empty outgoing host is no outgoing server"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Outgoing": { "Host": "" }, "Auth": { "PasswordEnvVar": "P" } }"""), []),
        ["an empty outgoing host granted send"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read, Send", "Outgoing": { "Host": " " }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailSend]),
        ["an empty address"] = (Account("""{ "Provider": "Gmail", "Address": "", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailAddress]),
        ["an empty password variable"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "" } }"""), [ValidationCodes.EmailAuth]),
        ["empty values the engine reads as absent"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "SaveSentCopy": "", "TimeoutSeconds": "", "Incoming": { "Protocol": "", "Port": "", "Security": "" }, "Auth": { "Method": "", "PasswordEnvVar": "P", "Username": "", "Tenant": "" } }"""), []),

        ["a text where the incoming object stands"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": "x", "Auth": { "PasswordEnvVar": "P" } }"""), []),
        ["a text where the send object stands"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Send": "x", "Outgoing": 3, "Auth": { "PasswordEnvVar": "P" } }"""), []),
        ["a text where the auth object stands"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": "x" }"""), [ValidationCodes.EmailAuth]),
        ["a text where the account stands"] = ("""{ "Accounts": { "work": "x" } }""",
            [ValidationCodes.EmailAddress, ValidationCodes.EmailRights, ValidationCodes.EmailServer, ValidationCodes.EmailAuth]),

        ["a port written in words"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Port": "abc" }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["a port with a decimal point"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Outgoing": { "Port": 465.0 }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["a port written as text"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Port": " 1993 " }, "Auth": { "PasswordEnvVar": "P" } }"""), []),
        ["a blank port"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Port": "  " }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["a switch that is neither true nor false"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "SaveSentCopy": "yes", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["a switch written as text"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "SaveSentCopy": " TRUE ", "Auth": { "PasswordEnvVar": "P" } }"""), []),
        ["a quota written in words and a timeout with its unit"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "TimeoutSeconds": "5s", "Send": { "MaxPerHour": "ten", "MaxRecipients": true }, "Auth": { "PasswordEnvVar": "P" } }"""),
            [ValidationCodes.EmailValue, ValidationCodes.EmailValue, ValidationCodes.EmailValue]),
        ["a provider written as its number"] = (Account("""{ "Provider": 1, "Address": "me@gmail.com", "Rights": 9, "Incoming": { "Security": " StartTls " }, "Auth": { "PasswordEnvVar": "P" } }"""), []),
        ["members separated by commas"] = (Account("""{ "Provider": "Custom, Gmail", "Address": "me@gmail.com", "Rights": "None, read , SEND", "Auth": { "Method": "+0", "PasswordEnvVar": "P" } }"""), []),
        ["a member left empty between commas"] = (Account("""{ "Provider": "Gmail,", "Address": "me@gmail.com", "Rights": "Read,", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue, ValidationCodes.EmailValue]),
        ["a provider number the engine does not define"] = (Account("""{ "Provider": 3, "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["rights with a bit the engine does not define"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": 64, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailValue]),
        ["rights written as a list"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": ["Read"], "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailRights]),
        ["an unreadable value hides the rules that follow"] = (Account("""{ "Provider": "Yahoo", "Incoming": { "Port": "abc" } }"""), [ValidationCodes.EmailValue, ValidationCodes.EmailValue]),

        ["a key no account carries"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Note": "mine", "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailKey]),
        ["a misspelt object"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incomming": { "Port": 993 }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailKey]),
        ["a key no incoming side carries"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Prot": "Imap" }, "Auth": { "PasswordEnvVar": "P" } }"""), [ValidationCodes.EmailKey]),
        ["a key no send policy carries and a value no one reads"] = (Account("""{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Reed", "Send": { "Quota": 5 }, "Auth": { "Secret": { "Value": "x" } } }"""),
            [ValidationCodes.EmailKey, ValidationCodes.EmailKey, ValidationCodes.EmailValue]),

        ["keys written in lower case"] = ("""
            { "screening": { "withholdrejected": false },
              "accounts": { "work": { "provider": "Outlook", "address": "me@hotmail.com", "rights": "Read, Send",
                "incoming": { "protocol": "Imap", "security": "StartTls" }, "outgoing": { "port": 2525 },
                "auth": { "clientid": "client", "tenant": "organizations", "username": "me" }, "send": { "allowedrecipients": ["*@example.com"] } } } }
            """, []),
        ["keys written in upper case over a broken account"] = ("""
            { "ACCOUNTS": { "work": { "PROVIDER": "Gmail", "ADDRESS": "me.gmail.com", "RIGHTS": "Read, Send",
                "INCOMING": { "HOST": "", "PORT": 0 }, "OUTGOING": { "HOST": "smtp.example.com", "SECURITY": "None" },
                "AUTH": { "METHOD": "OAuth2" }, "SEND": { "ALLOWEDRECIPIENTS": ["*.b.c"], "MAXPERHOUR": 0 }, "TIMEOUTSECONDS": 0 } } }
            """, [ValidationCodes.EmailAddress, ValidationCodes.EmailServer, ValidationCodes.EmailServer, ValidationCodes.EmailAuth, ValidationCodes.EmailSend, ValidationCodes.EmailSend, ValidationCodes.EmailServer]),
        ["a value the engine cannot read under a key in lower case"] = (Account("""{ "provider": "Gmail", "rights": "Reed", "incoming": { "port": "nine" } }"""), [ValidationCodes.EmailValue, ValidationCodes.EmailValue]),
        ["a screening switch in lower case the engine cannot read"] = ("""
            { "screening": { "withholdrejected": "maybe" },
              "accounts": { "work": { "provider": "Gmail", "address": "me@gmail.com", "rights": "Read", "auth": { "passwordenvvar": "P" } } } }
            """, [ValidationCodes.EmailScreening]),
        ["a screening switch the engine cannot read"] = ("""
            { "Screening": { "WithholdRejected": "maybe" },
              "Accounts": { "work": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } } } }
            """, [ValidationCodes.EmailScreening]),
        ["a screening switch written as a number over a broken account"] = ("""
            { "Screening": { "WithholdRejected": 1 },
              "Accounts": { "work": { "Provider": "Gmail", "Note": "mine" } } }
            """, [ValidationCodes.EmailScreening, ValidationCodes.EmailKey]),
        ["a screening switch written as text"] = ("""
            { "Screening": { "WithholdRejected": " True " },
              "Accounts": { "work": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } } } }
            """, []),
        ["an empty screening switch"] = ("""
            { "Screening": { "WithholdRejected": "" },
              "Accounts": { "work": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } } } }
            """, []),
    };

    public static TheoryData<string> DefinitionRows() => new(Definitions.Keys);

    public static TheoryData<string> FileRows() => new(Files.Keys);

    [Theory]
    [MemberData(nameof(DefinitionRows))]
    public void Studio_and_the_engine_say_the_same_of_an_account_studio_writes(string row)
    {
        var (definition, codes) = Definitions[row];
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(definition);

        var findings = EmailAccountRules.Check(definition.Name, definition);

        Assert.Equal(codes, findings.Select(finding => finding.Code));
        AssertSameAsTheEngine(document, definition.Name, findings, definition);

        // The file Studio wrote reads back as the account it was given.
        Assert.Equal(findings, EmailAccountRules.Check(document, definition.Name));
    }

    [Theory]
    [MemberData(nameof(FileRows))]
    public void Studio_and_the_engine_say_the_same_of_an_account_written_by_hand(string row)
    {
        var (section, codes) = Files[row];
        var document = AppSettingsDocument.Parse($$"""{ "Orkeon": { "Tools": { "Email": {{section}} } } }""");

        var findings = EmailAccountRules.Check(document, Name);

        Assert.Equal(codes, findings.Select(finding => finding.Code));
        AssertSameAsTheEngine(document, Name, findings, document.Email.GetAccount(Name) ?? new EmailAccountDefinition { Name = Name });
    }

    [Fact]
    public void Every_finding_on_an_account_is_a_warning_at_a_path_under_the_account()
    {
        foreach (var (definition, _) in Definitions.Values)
        {
            foreach (var finding in EmailAccountRules.Check(definition.Name, definition))
            {
                Assert.Equal(ValidationSeverity.Warning, finding.Severity);
                Assert.StartsWith($"{EmailSection.AccountsPath}:{definition.Name}", finding.Path, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void An_account_the_file_does_not_hold_has_no_finding()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(Gmail with { Address = null });

        Assert.Empty(EmailAccountRules.Check(document, "other"));
        Assert.Empty(EmailAccountRules.Check(AppSettingsDocument.CreateEmpty(), Name));
    }

    /// <summary>
    /// The verdict, the problems by family and the effective values, against what the engine's
    /// registry returns for the account once its binder has read <paramref name="document"/>.
    /// </summary>
    private static void AssertSameAsTheEngine(
        AppSettingsDocument document, string name, IReadOnlyList<ValidationMessage> findings, EmailAccountDefinition definition)
    {
        var options = Bind(document);
        var resolution = new EmailAccountRegistry(Options.Create(options)).Inspect(name);

        // Usable or set aside.
        Assert.Equal(resolution.Account is null, findings.Count > 0);

        // The same number of problems in each family, and the same first sentence.
        var engine = resolution.Problems.ToLookup(FamilyOf);
        var studio = findings.ToLookup(finding => finding.Code, finding => finding.Text);
        Assert.Equal(engine.Select(family => family.Key).Order(StringComparer.Ordinal), studio.Select(family => family.Key).Order(StringComparer.Ordinal));
        foreach (var family in engine)
        {
            Assert.Equal(family.Count(), studio[family.Key].Count());
            Assert.Equal(family.First(), studio[family.Key].First());
        }

        // What a field left blank is worth. A value the binder could not read leaves the engine an
        // empty account, of which it says nothing more.
        if (options.SectionProblems.Count > 0 || options.AccountProblems.ContainsKey(name))
            return;

        var effective = EmailAccountEffective.Of(definition);
        Assert.Equal(EmailAccountResolver.EffectiveAuthMethod(options.Accounts[name]).ToString(), effective.AuthMethod);
        if (resolution.Account is { } account)
            EmailAccountEffectiveTests.AssertSameAsTheEngine(account, effective);
    }

    /// <summary>The code Studio files an engine sentence under: the families of <see cref="ValidationCodes"/>.</summary>
    private static string FamilyOf(string problem) => problem switch
    {
        _ when problem.Contains(" is not an account setting: ", StringComparison.Ordinal) => ValidationCodes.EmailKey,
        _ when problem.StartsWith(ConfigurationKeys.ToolsEmail + ":Screening:", StringComparison.Ordinal) => ValidationCodes.EmailScreening,
        _ when problem.Contains("' is not one of ", StringComparison.Ordinal)
            || problem.Contains("' is not a list of ", StringComparison.Ordinal)
            || problem.EndsWith("' is neither true nor false", StringComparison.Ordinal)
            || problem.EndsWith("' is not a whole number", StringComparison.Ordinal) => ValidationCodes.EmailValue,
        _ when problem.StartsWith("the account name ", StringComparison.Ordinal) => ValidationCodes.EmailName,
        _ when problem.StartsWith("Address ", StringComparison.Ordinal) => ValidationCodes.EmailAddress,
        _ when problem.StartsWith("Rights is required", StringComparison.Ordinal) => ValidationCodes.EmailRights,
        _ when problem.StartsWith("Rights grant Send", StringComparison.Ordinal)
            || problem.StartsWith("Send:", StringComparison.Ordinal) => ValidationCodes.EmailSend,
        _ when problem.StartsWith("Incoming:", StringComparison.Ordinal)
            || problem.StartsWith("Outgoing:", StringComparison.Ordinal)
            || problem.StartsWith("an account read through Graph", StringComparison.Ordinal)
            || problem.StartsWith("TimeoutSeconds ", StringComparison.Ordinal) => ValidationCodes.EmailServer,
        _ when problem.StartsWith("Auth:", StringComparison.Ordinal)
            || problem.StartsWith("Outlook.com ", StringComparison.Ordinal)
            || problem.StartsWith("OAuth2 is available", StringComparison.Ordinal) => ValidationCodes.EmailAuth,
        _ => throw new Xunit.Sdk.XunitException($"The engine says something Studio files under no code: {problem}"),
    };

    private static string Account(string account) => $$"""{ "Accounts": { "work": {{account}} } }""";

    private static EmailToolsOptions Bind(AppSettingsDocument document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJson()));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new EmailToolsOptions();

        EmailOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsEmail), options);
        return options;
    }
}
