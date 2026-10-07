using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;
using Orkeon.Tools.Email;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-66 — the e-mail accounts in the settings check: each finding with its code, its
/// severity and its path. A broken account never stops a run — the engine sets it aside and says
/// so when a tool names it —, so every finding is a warning and the file saves; the one error is
/// the pair of names, or of keys, the configuration cannot read at all.
/// </summary>
public sealed partial class EmailValidationTests : IDisposable
{
    private const string Accounts = EmailSection.AccountsPath;

    private const string GmailAccount = """{ "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read, Organize, Draft", "Auth": { "PasswordEnvVar": "GMAIL_APP_PASSWORD" } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-studio-mail-" + Guid.NewGuid().ToString("N"));

    public EmailValidationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>A whole settings file around an <c>Orkeon:Tools:Email</c> section, valid in everything else.</summary>
    private static string Settings(string email) => $$"""
        { "Llm": { "Model": "m", "BaseUrl": "http://localhost:11434" },
          "Orkeon": { "FileSystem": { "Mounts": ["/tmp:/data:ro"] }, "Tools": { "Email": {{email}} } } }
        """;

    private static IReadOnlyList<ValidationMessage> Validate(string email, ValidationScope scope = ValidationScope.Editing) =>
        new AppSettingsValidator(new FakeDirectoryProbe("/tmp")).Validate(AppSettingsDocument.Parse(Settings(email)), scope);

    private static IReadOnlyList<ValidationMessage> Mail(IEnumerable<ValidationMessage> messages) =>
        [.. messages.Where(message => message.Code.StartsWith("STUDIO-MAIL-", StringComparison.Ordinal))];

    [Fact]
    public void A_well_formed_section_raises_nothing()
    {
        var messages = Validate($$"""
            { "DefaultAccount": "Gmail", "Screening": { "WithholdRejected": true }, "CredentialsDirectory": "/srv/credentials",
              "Accounts": {
                "gmail": {{GmailAccount}},
                "hotmail": { "Provider": "Outlook", "Address": "me@hotmail.com", "Rights": "Read, Send", "Auth": { "ClientId": "client" }, "Send": { "AllowedRecipients": ["*@example.com"] } }
              } }
            """);

        Assert.Empty(messages);
    }

    [Fact]
    public void A_file_without_the_section_raises_nothing_about_mail()
    {
        var messages = new AppSettingsValidator(new FakeDirectoryProbe()).Validate(AppSettingsDocument.Parse("""{ "Orkeon": { "Tools": { } } }"""));

        Assert.Empty(Mail(messages));
    }

    [Fact]
    public void An_incomplete_account_is_a_set_of_warnings_at_their_paths_and_the_file_still_saves()
    {
        var messages = Validate("""{ "Accounts": { "work": { "Address": "me.example.com", "Send": { "MaxPerHour": 0 } } } }""", ValidationScope.Saving);

        Assert.DoesNotContain(messages, message => message.Severity == ValidationSeverity.Error);
        Assert.All(Mail(messages), message => Assert.Equal(ValidationSeverity.Warning, message.Severity));
        Assert.Equal(
            [
                (ValidationCodes.EmailAddress, $"{Accounts}:work:Address"),
                (ValidationCodes.EmailRights, $"{Accounts}:work:Rights"),
                (ValidationCodes.EmailServer, $"{Accounts}:work:Incoming:Host"),
                (ValidationCodes.EmailAuth, $"{Accounts}:work:Auth:PasswordEnvVar"),
                (ValidationCodes.EmailSend, $"{Accounts}:work:Send:MaxPerHour"),
            ],
            Mail(messages).Select(message => (message.Code, message.Path!)));
    }

    [Fact]
    public void A_finding_says_what_the_engine_will_say_when_a_tool_names_the_account()
    {
        var email = """{ "Accounts": { "work": { "Provider": "Outlook", "Address": "me@hotmail.com", "Rights": "Read", "Auth": { "Method": "Password" } } } }""";

        var messages = Mail(Validate(email));

        var misconfigured = Assert.Throws<EmailToolException>(() => Registry(email).Resolve("work"));
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Contains(message.Text, misconfigured.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Each_rule_of_an_account_lands_on_the_key_that_fixes_it()
    {
        var messages = Mail(Validate("""
            { "Accounts": {
                "a b": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } },
                "graph": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Incoming": { "Protocol": "Graph" }, "Outgoing": { "Protocol": "Smtp" }, "Auth": { "PasswordEnvVar": "P" }, "TimeoutSeconds": 0 },
                "ports": { "Address": "me@example.com", "Rights": "Read, Send", "Incoming": { "Host": "imap.example.com", "Port": 0, "Security": "None" }, "Outgoing": { "Protocol": "Graph", "Port": 70000 }, "Auth": { "PasswordEnvVar": "P" } },
                "oauth": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "ClientId": "client" }, "Send": { "AllowedRecipients": ["*.b.c"], "MaxRecipients": 0 } },
                "tenant": { "Provider": "Outlook", "Address": "me@hotmail.com", "Rights": "Read", "Auth": { "ClientId": "client", "Tenant": "my tenant" } },
                "custom": { "Address": "me@example.com", "Rights": "Read", "Incoming": { "Host": "imap.example.com" }, "Auth": { "Method": "OAuth2" } }
              } }
            """));

        Assert.Equal(
            [
                (ValidationCodes.EmailName, $"{Accounts}:a b"),
                (ValidationCodes.EmailServer, $"{Accounts}:graph:Incoming:Protocol"),
                (ValidationCodes.EmailServer, $"{Accounts}:graph:Outgoing:Protocol"),
                (ValidationCodes.EmailServer, $"{Accounts}:graph:TimeoutSeconds"),
                (ValidationCodes.EmailServer, $"{Accounts}:ports:Incoming:Port"),
                (ValidationCodes.EmailServer, $"{Accounts}:ports:Incoming:Security"),
                (ValidationCodes.EmailServer, $"{Accounts}:ports:Outgoing:Protocol"),
                (ValidationCodes.EmailSend, $"{Accounts}:ports:Outgoing:Host"),
                (ValidationCodes.EmailAuth, $"{Accounts}:oauth:Auth:ClientSecretEnvVar"),
                (ValidationCodes.EmailSend, $"{Accounts}:oauth:Send:AllowedRecipients"),
                (ValidationCodes.EmailSend, $"{Accounts}:oauth:Send:MaxRecipients"),
                (ValidationCodes.EmailAuth, $"{Accounts}:tenant:Auth:Tenant"),
                (ValidationCodes.EmailAuth, $"{Accounts}:custom:Auth:ClientId"),
            ],
            messages.Select(message => (message.Code, message.Path!)));
    }

    [Fact]
    public void A_key_no_account_carries_and_a_value_the_engine_cannot_read_are_warnings_on_that_key()
    {
        var messages = Mail(Validate($$"""
            { "Accounts": {
                "noted": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P", "Hint": "x" }, "Note": "mine" },
                "words": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Reed", "Incoming": { "Port": "nine" }, "Auth": { "PasswordEnvVar": "P" } },
                "gmail": {{GmailAccount}}
              } }
            """));

        Assert.All(messages, message => Assert.Equal(ValidationSeverity.Warning, message.Severity));
        Assert.Equal(
            [
                (ValidationCodes.EmailKey, $"{Accounts}:noted:Auth:Hint"),
                (ValidationCodes.EmailKey, $"{Accounts}:noted:Note"),
                (ValidationCodes.EmailValue, $"{Accounts}:words:Rights"),
                (ValidationCodes.EmailValue, $"{Accounts}:words:Incoming:Port"),
            ],
            messages.Select(message => (message.Code, message.Path!)));
        Assert.Contains("Note is not an account setting", messages[1].Text, StringComparison.Ordinal);
        Assert.Equal("Incoming:Port 'nine' is not a whole number", messages[3].Text);
    }

    [Fact]
    public void A_default_account_that_names_no_account_is_a_warning_in_the_words_of_the_engine()
    {
        var email = $$"""{ "DefaultAccount": "perso", "Accounts": { "gmail": {{GmailAccount}}, "Work": {{GmailAccount}} } }""";

        var message = Assert.Single(Mail(Validate(email)));

        Assert.Equal(ValidationCodes.EmailDefault, message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
        Assert.Equal($"{EmailSection.SectionPath}:DefaultAccount", message.Path);
        Assert.Equal(Assert.Throws<EmailToolException>(() => Registry(email).Resolve(null)).Message, message.Text);
    }

    [Fact]
    public void A_default_account_is_found_whatever_its_case_and_a_blank_one_names_nothing_to_find()
    {
        Assert.Empty(Mail(Validate($$"""{ "DefaultAccount": " GMAIL ", "Accounts": { "gmail": {{GmailAccount}} } }""")));
        Assert.Empty(Mail(Validate($$"""{ "DefaultAccount": "  ", "Accounts": { "gmail": {{GmailAccount}} } }""")));
        Assert.Equal(ValidationCodes.EmailDefault, Assert.Single(Mail(Validate("""{ "DefaultAccount": "gmail" }"""))).Code);
    }

    [Fact]
    public void A_screening_switch_the_engine_cannot_read_is_a_warning_on_the_section_and_on_every_account()
    {
        var email = $$"""
            { "Screening": { "WithholdRejected": "maybe" },
              "Accounts": { "gmail": {{GmailAccount}}, "work": { "Address": "me@example.com" } } }
            """;

        var messages = Mail(Validate(email));

        Assert.All(messages, message => Assert.Equal((ValidationCodes.EmailScreening, ValidationSeverity.Warning), (message.Code, message.Severity)));
        Assert.Equal(
            [$"{EmailSection.SectionPath}:Screening:WithholdRejected", $"{Accounts}:gmail", $"{Accounts}:work"],
            messages.Select(message => message.Path));

        // Every account is set aside on that one sentence, the complete one and the broken one alike.
        var registry = Registry(email);
        Assert.All(messages, message => Assert.Equal([message.Text], registry.Inspect("gmail").Problems));
        Assert.Equal(registry.Inspect("gmail").Problems, registry.Inspect("work").Problems);
    }

    [Fact]
    public void A_password_variable_that_is_no_variable_name_is_a_warning_that_never_repeats_what_was_typed()
    {
        var messages = Mail(Validate("""
            { "Accounts": {
                "gmail": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "abcd efgh ijkl mnop" } },
                "oauth": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "ClientId": "client", "ClientSecretEnvVar": "GOCSPX=secret" } }
              } }
            """));

        Assert.Equal(
            [
                (ValidationCodes.EmailAuth, ValidationSeverity.Warning, $"{Accounts}:gmail:Auth:PasswordEnvVar"),
                (ValidationCodes.EmailAuth, ValidationSeverity.Warning, $"{Accounts}:oauth:Auth:ClientSecretEnvVar"),
            ],
            messages.Select(message => (message.Code, message.Severity, message.Path!)));
        Assert.DoesNotContain("abcd", messages[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("GOCSPX", messages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_names_equal_but_for_the_case_are_the_one_error_and_no_run_can_read_that_file()
    {
        var email = """
            { "Accounts": {
                "Perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "P" } },
                "perso": { "Provider": "Gmail", "Address": "you@gmail.com", "Rights": "Read", "Auth": { "PasswordEnvVar": "Q" } }
              } }
            """;

        var message = Assert.Single(Mail(Validate(email)));

        Assert.Equal(ValidationCodes.EmailDuplicate, message.Code);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
        Assert.Equal($"{Accounts}:perso", message.Path);
        Assert.Contains("'Perso'", message.Text, StringComparison.Ordinal);

        // Why it blocks the save: the run's own composition refuses the file, e-mail crew or not.
        var refusal = Assert.Throws<InvalidDataException>(() => Compose(email));
        Assert.Contains("A duplicate key", refusal.InnerException?.Message, StringComparison.Ordinal);
        Assert.Contains("Accounts:perso:", refusal.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_names_equal_but_for_the_case_that_share_no_key_are_read_as_one_account_which_is_a_warning()
    {
        var email = """
            { "Accounts": {
                "Perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Auth": { "PasswordEnvVar": "P" } },
                "perso": { "Rights": "Read", "Auth": { "Username": "me" } }
              } }
            """;

        // Neither half is judged on its own: the run never sees one without the other.
        var duplicate = Assert.Single(Mail(Validate(email)));

        Assert.Equal(ValidationCodes.EmailDuplicate, duplicate.Code);
        Assert.Equal(ValidationSeverity.Warning, duplicate.Severity);
        Assert.Equal($"{Accounts}:perso", duplicate.Path);

        // The run reads the file, and one account made of both.
        var configuration = Compose(email);
        var merged = Assert.Single(configuration.GetSection(Accounts).GetChildren());
        Assert.Equal("me@gmail.com", merged["Address"]);
        Assert.Equal("Read", merged["Rights"]);
        Assert.Equal("me", merged["Auth:Username"]);
    }

    [Fact]
    public void A_third_name_equal_but_for_the_case_is_compared_with_every_one_before_it()
    {
        var email = """
            { "Accounts": {
                "Perso": { "Address": "me@gmail.com" },
                "perso": { "Rights": "Read" },
                "PERSO": { "Rights": "Read, Send" }
              } }
            """;

        var messages = Mail(Validate(email));

        Assert.Equal(
            [
                (ValidationCodes.EmailDuplicate, ValidationSeverity.Warning, $"{Accounts}:perso"),
                (ValidationCodes.EmailDuplicate, ValidationSeverity.Error, $"{Accounts}:PERSO"),
            ],
            messages.Select(message => (message.Code, message.Severity, message.Path!)));
        Assert.Contains("Rights", messages[1].Text, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Compose(email));
    }

    [Fact]
    public void A_section_whose_keys_are_in_another_case_is_judged_as_the_engine_reads_it()
    {
        var sound = Mail(Validate("""
            { "defaultaccount": "WORK", "screening": { "withholdrejected": "true" },
              "accounts": { "work": { "provider": "Gmail", "address": "me@gmail.com", "rights": "Read", "auth": { "passwordenvvar": "P" } } } }
            """));
        var email = """
            { "DEFAULTACCOUNT": "perso", "SCREENING": { "WITHHOLDREJECTED": "maybe" },
              "ACCOUNTS": { "work": { "PROVIDER": "Gmail", "ADDRESS": "me@gmail.com", "RIGHTS": "Read", "AUTH": { "PASSWORDENVVAR": "P" } } } }
            """;

        var messages = Mail(Validate(email));

        Assert.Empty(sound);
        Assert.Equal(
            [
                (ValidationCodes.EmailScreening, $"{EmailSection.SectionPath}:Screening:WithholdRejected"),
                (ValidationCodes.EmailDefault, $"{EmailSection.SectionPath}:DefaultAccount"),
                (ValidationCodes.EmailScreening, $"{Accounts}:work"),
            ],
            messages.Select(message => (message.Code, message.Path!)));
        Assert.Equal([messages[0].Text], Registry(email).Inspect("work").Problems);
        Assert.Equal(Assert.Throws<EmailToolException>(() => Registry(email).Resolve(null)).Message, messages[1].Text);
    }

    [Fact]
    public void The_same_file_is_judged_the_same_while_editing_and_when_saving()
    {
        var email = """{ "DefaultAccount": "none", "Accounts": { "work": { "Provider": "Gmail", "Note": "mine" } } }""";

        Assert.Equal(Mail(Validate(email, ValidationScope.Editing)), Mail(Validate(email, ValidationScope.Saving)));
        Assert.Equal(2, Mail(Validate(email)).Count);
    }

    [Fact]
    public void An_account_under_a_blank_name_or_holding_no_object_is_judged_and_nothing_throws()
    {
        var messages = Mail(Validate("""{ "Accounts": { " ": { }, "text": "x", "none": null } }"""));

        Assert.Equal(ValidationCodes.EmailName, messages[0].Code);
        Assert.Equal($"{Accounts}: ", messages[0].Path);
        Assert.Equal(4, messages.Count(message => message.Path!.StartsWith($"{Accounts}:text", StringComparison.Ordinal)));
        Assert.Equal(4, messages.Count(message => message.Path!.StartsWith($"{Accounts}:none", StringComparison.Ordinal)));
    }

    /// <summary>The engine's registry over the section, bound by the engine's binder.</summary>
    private static EmailAccountRegistry Registry(string email)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Settings(email)));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new EmailToolsOptions();

        EmailOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsEmail), options);
        return new EmailAccountRegistry(Options.Create(options));
    }

    /// <summary>The configuration a run composes over the settings file, sources and order included.</summary>
    private IConfiguration Compose(string email)
    {
        var path = Path.Combine(_root, AppSettingsDocument.FileName);
        File.WriteAllText(path, Settings(email));
        return RunnerSettings.ComposeSources(new ConfigurationBuilder(), path).Build();
    }
}
