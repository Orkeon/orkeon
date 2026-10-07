using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Keys equal but for the case, written by hand in one object of <c>Orkeon:Tools:Email</c>
/// (<c>Provider</c> and <c>provider</c>). The severity is the run's own verdict, read from the
/// configuration a run composes over the file: an error when it refuses the file, a warning when
/// it merges the two.
/// </summary>
public sealed partial class EmailValidationTests
{
    private const string Section = EmailSection.SectionPath;

    /// <summary>A section with one pair of twin keys, the severity of its finding and its path.</summary>
    public static TheoryData<string, ValidationSeverity, string> TwinKeys => new()
    {
        // Two values: the configuration refuses the second.
        { """{ "Accounts": { "work": { "Provider": "Gmail", "provider": "Outlook" } } }""", ValidationSeverity.Error, $"{Accounts}:work:Provider" },
        { """{ "Accounts": { "work": { "Provider": null, "provider": "Gmail" } } }""", ValidationSeverity.Error, $"{Accounts}:work:Provider" },
        { """{ "Accounts": { "work": { "Incoming": { "Host": "a.example.com", "host": "b.example.com" } } } }""", ValidationSeverity.Error, $"{Accounts}:work:Incoming:Host" },
        { """{ "Accounts": { "work": { "Auth": { "PasswordEnvVar": "P", "passwordenvvar": "Q" } } } }""", ValidationSeverity.Error, $"{Accounts}:work:Auth:PasswordEnvVar" },
        { """{ "Accounts": { "work": { "Send": { "AllowedRecipients": ["a@x.org"], "allowedrecipients": ["b@x.org"] } } } }""", ValidationSeverity.Error, $"{Accounts}:work:Send:AllowedRecipients" },
        { $$"""{ "DefaultAccount": "gmail", "defaultaccount": "gmail", "Accounts": { "gmail": {{GmailAccount}} } }""", ValidationSeverity.Error, $"{Section}:DefaultAccount" },
        { """{ "Screening": { "WithholdRejected": true, "withholdrejected": false } }""", ValidationSeverity.Error, $"{Section}:Screening:WithholdRejected" },

        // Two objects: refused when both set one same key, merged otherwise.
        { """{ "Accounts": { "work": { "Incoming": { "Host": "a.example.com" }, "incoming": { "HOST": "b.example.com" } } } }""", ValidationSeverity.Error, $"{Accounts}:work:Incoming" },
        { """{ "Accounts": { "work": { "Incoming": { "Host": "a.example.com" }, "incoming": { "Port": 993 } } } }""", ValidationSeverity.Warning, $"{Accounts}:work:Incoming" },
        { """{ "Screening": { "WithholdRejected": true }, "screening": { "withholdrejected": false } }""", ValidationSeverity.Error, $"{Section}:Screening" },
        { """{ "Screening": { "WithholdRejected": true }, "screening": { "Note": 1 } }""", ValidationSeverity.Warning, $"{Section}:Screening" },
        { $$"""{ "Accounts": { "gmail": {{GmailAccount}} }, "accounts": { "GMAIL": { "provider": "Gmail" } } }""", ValidationSeverity.Error, $"{Section}:Accounts" },
        { $$"""{ "Accounts": { "gmail": {{GmailAccount}} }, "accounts": { "work": {{GmailAccount}} } }""", ValidationSeverity.Warning, $"{Section}:Accounts" },

        // A value beside an object: the configuration holds both, the value and the keys under it.
        { """{ "Accounts": { "work": { "Incoming": "x", "incoming": { "Port": 993 } } } }""", ValidationSeverity.Warning, $"{Accounts}:work:Incoming" },

        // An empty object or list holds its key: a value written after it is refused, one written before it is replaced.
        { """{ "Accounts": { "work": { "Incoming": { }, "incoming": "x" } } }""", ValidationSeverity.Error, $"{Accounts}:work:Incoming" },
        { """{ "Accounts": { "work": { "Incoming": "x", "incoming": { } } } }""", ValidationSeverity.Warning, $"{Accounts}:work:Incoming" },
        { """{ "Accounts": { "work": { "Send": { "AllowedRecipients": [ ], "allowedrecipients": "a@x.org" } } } }""", ValidationSeverity.Error, $"{Accounts}:work:Send:AllowedRecipients" },

        // The path names the key as Studio spells it, and a key it does not know as the file first spells it.
        { """{ "Accounts": { "work": { "PROVIDER": "Gmail", "provider": "Gmail" } } }""", ValidationSeverity.Error, $"{Accounts}:work:Provider" },
        { """{ "Accounts": { "work": { "note": "mine", "Note": "yours" } } }""", ValidationSeverity.Error, $"{Accounts}:work:note" },
    };

    [Theory]
    [MemberData(nameof(TwinKeys))]
    public void Two_keys_equal_but_for_the_case_are_an_error_when_the_run_refuses_the_file_and_a_warning_when_it_merges_them(
        string email, ValidationSeverity severity, string path)
    {
        // Twin keys are all Studio says of the object that carries them: the run reads none of them alone.
        var twin = Assert.Single(Mail(Validate(email)));

        Assert.Equal((ValidationCodes.EmailTwin, severity, path), (twin.Code, twin.Severity, twin.Path));

        // The severity is the run's verdict on that very file.
        Assert.Equal(RunReads(email) ? ValidationSeverity.Warning : ValidationSeverity.Error, twin.Severity);
    }

    [Fact]
    public void The_finding_names_both_spellings_and_the_key_both_set()
    {
        var values = Assert.Single(Mail(Validate("""{ "Accounts": { "work": { "Provider": "Gmail", "provider": "Outlook" } } }""")));
        var objects = Assert.Single(Mail(Validate("""{ "Accounts": { "work": { "Incoming": { "Port": 993 }, "incoming": { "port": 143 } } } }""")));
        var merged = Assert.Single(Mail(Validate("""{ "Accounts": { "work": { "Incoming": { "Host": "a.example.com" }, "incoming": { "Port": 993 } } } }""")));

        Assert.Contains("'Provider' and 'provider'", values.Text, StringComparison.Ordinal);
        Assert.Contains("'Incoming' and 'incoming'", objects.Text, StringComparison.Ordinal);
        Assert.Contains("both set port", objects.Text, StringComparison.Ordinal);
        Assert.Contains("'Incoming' and 'incoming'", merged.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_objects_equal_but_for_the_case_that_share_no_key_are_one_to_the_run_and_studio_reads_one_of_them()
    {
        var email = """
            { "Accounts": { "work": { "Incoming": { "Host": "imap.example.com" }, "incoming": { "Port": 143 } } } }
            """;

        var twin = Assert.Single(Mail(Validate(email, ValidationScope.Saving)));

        Assert.Equal((ValidationCodes.EmailTwin, ValidationSeverity.Warning), (twin.Code, twin.Severity));

        // The run reads one Incoming made of both; the form shows the half it finds first.
        var account = Assert.Single(Compose(email).GetSection(Accounts).GetChildren());
        Assert.Equal("imap.example.com", account["Incoming:Host"]);
        Assert.Equal("143", account["Incoming:Port"]);
        Assert.Null(AppSettingsDocument.Parse(Settings(email)).Email.GetAccount("work")!.IncomingPort);
    }

    [Fact]
    public void A_third_key_equal_but_for_the_case_is_compared_with_every_one_before_it()
    {
        var email = """
            { "Accounts": { "work": { "Incoming": { "Host": "a.example.com" }, "incoming": { "Port": 993 }, "INCOMING": { "port": 143 } } } }
            """;

        var messages = Mail(Validate(email));

        Assert.Equal(
            [
                (ValidationCodes.EmailTwin, ValidationSeverity.Warning, $"{Accounts}:work:Incoming"),
                (ValidationCodes.EmailTwin, ValidationSeverity.Error, $"{Accounts}:work:Incoming"),
            ],
            messages.Select(message => (message.Code, message.Severity, message.Path!)));
        Assert.Contains("'Incoming' and 'incoming'", messages[0].Text, StringComparison.Ordinal);
        Assert.Contains("'Incoming' and 'INCOMING'", messages[1].Text, StringComparison.Ordinal);
        Assert.False(RunReads(email));
    }

    [Fact]
    public void Twin_keys_at_two_depths_are_each_said_once_where_they_stand()
    {
        var email = """
            { "Accounts": { "work": { "Incoming": { "Host": "a.example.com", "host": "b.example.com" }, "incoming": { "Port": 993 } } } }
            """;

        var messages = Mail(Validate(email));

        // The two Incoming share no key: what the run refuses is the pair inside the first.
        Assert.Equal(
            [
                (ValidationCodes.EmailTwin, ValidationSeverity.Warning, $"{Accounts}:work:Incoming"),
                (ValidationCodes.EmailTwin, ValidationSeverity.Error, $"{Accounts}:work:Incoming:Host"),
            ],
            messages.Select(message => (message.Code, message.Severity, message.Path!)));
        Assert.False(RunReads(email));
    }

    [Fact]
    public void The_twin_keys_of_an_account_are_what_its_row_shows_and_all_it_shows()
    {
        var document = AppSettingsDocument.Parse(Settings($$"""
            { "Accounts": {
                "gmail": {{GmailAccount}},
                "work": { "Provider": "Gmail", "provider": "Outlook", "Note": "mine" }
              } }
            """));

        var twin = Assert.Single(EmailAccountRules.Check(document, "work"));

        Assert.Equal(
            (ValidationCodes.EmailTwin, ValidationSeverity.Error, $"{Accounts}:work:Provider"),
            (twin.Code, twin.Severity, twin.Path));
        Assert.Empty(EmailAccountRules.Check(document, "gmail"));
    }

    [Fact]
    public void Twin_keys_inside_an_account_whose_name_has_a_twin_are_still_said()
    {
        var email = """
            { "Accounts": {
                "Perso": { "Address": "me@gmail.com", "address": "you@gmail.com" },
                "perso": { "Rights": "Read" }
              } }
            """;

        var messages = Mail(Validate(email));

        // The two names share no key: without the pair inside the first, the run would read the file.
        Assert.Equal(
            [
                (ValidationCodes.EmailTwin, ValidationSeverity.Error, $"{Accounts}:Perso:Address"),
                (ValidationCodes.EmailDuplicate, ValidationSeverity.Warning, $"{Accounts}:perso"),
            ],
            messages.Select(message => (message.Code, message.Severity, message.Path!)));
        Assert.False(RunReads(email));
    }

    [Theory]
    [InlineData("""{ "Accounts": { "Perso": { }, "perso": "x" } }""", ValidationSeverity.Error)]
    [InlineData("""{ "Accounts": { "Perso": "x", "perso": { } } }""", ValidationSeverity.Warning)]
    public void Two_names_equal_but_for_the_case_are_judged_as_the_run_reads_an_empty_account(string email, ValidationSeverity severity)
    {
        var duplicate = Assert.Single(Mail(Validate(email)));

        Assert.Equal((ValidationCodes.EmailDuplicate, severity, $"{Accounts}:perso"), (duplicate.Code, duplicate.Severity, duplicate.Path));
        Assert.Equal(RunReads(email) ? ValidationSeverity.Warning : ValidationSeverity.Error, duplicate.Severity);
    }

    /// <summary>Whether the run's composition reads the file: the JSON provider refuses a key it already holds.</summary>
    private bool RunReads(string email)
    {
        try
        {
            Compose(email);
            return true;
        }
        catch (InvalidDataException refusal) when (refusal.InnerException is FormatException)
        {
            return false;
        }
    }
}
