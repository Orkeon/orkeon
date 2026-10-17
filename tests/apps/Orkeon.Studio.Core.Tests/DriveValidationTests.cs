using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// How the drive section plugs into <see cref="AppSettingsValidator"/>, on the model of
/// <see cref="EmailValidationTests"/>: every finding of an account is a warning — the run sets it
/// aside, the file saves —, the default account must name a declared one, and the one error is a
/// pair of names or keys equal but for the case that set one same key. <see cref="DriveSecretNames"/>
/// follows the e-mail convention for the variable of a client secret.
/// </summary>
public sealed class DriveValidationTests
{
    private static IReadOnlyList<ValidationMessage> Validate(string driveSection) =>
        new AppSettingsValidator().ValidateJson($$"""{ "Llm": { "Provider": "Ollama" }, "Orkeon": { "Tools": { "Drive": {{driveSection}} } } }""")
            .Where(message => message.Code.StartsWith("STUDIO-DRIVE-", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void A_usable_account_raises_nothing()
    {
        Assert.Empty(Validate("""{ "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } }"""));
    }

    [Fact]
    public void An_incomplete_account_is_a_warning_and_the_file_still_saves()
    {
        var findings = Validate("""{ "Accounts": { "work": { "Provider": "GoogleDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } }""");

        var finding = Assert.Single(findings);
        Assert.Equal(ValidationCodes.DriveAuth, finding.Code);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("Orkeon:Tools:Drive:Accounts:work:Auth:ClientSecretEnvVar", finding.Path);
    }

    [Fact]
    public void A_default_account_that_names_none_of_them_says_the_engines_sentence()
    {
        var finding = Assert.Single(Validate("""
            { "DefaultAccount": "office", "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } },
              "Archive": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } }
            """));

        Assert.Equal(ValidationCodes.DriveDefault, finding.Code);
        Assert.Equal("Unknown drive account 'office'. Configured: Archive, work.", finding.Text);
    }

    [Fact]
    public void Two_names_equal_but_for_the_case_are_an_error_when_they_set_one_same_key_and_a_warning_otherwise()
    {
        var clash = Validate("""{ "Accounts": { "work": { "Provider": "OneDrive" }, "WORK": { "Provider": "OneDrive" } } }""");
        var merge = Validate("""{ "Accounts": { "work": { "Provider": "OneDrive" }, "WORK": { "Rights": "Read" } } }""");

        var error = Assert.Single(clash, message => message.Code == ValidationCodes.DriveDuplicate);
        Assert.Equal(ValidationSeverity.Error, error.Severity);
        Assert.StartsWith("Drive accounts 'work' and 'WORK' differ only by case and both set Provider", error.Text, StringComparison.Ordinal);

        var warning = Assert.Single(merge, message => message.Code == ValidationCodes.DriveDuplicate);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Two_keys_equal_but_for_the_case_in_one_account_are_said_alone()
    {
        var findings = Validate("""{ "Accounts": { "work": { "Provider": "OneDrive", "provider": "GoogleDrive", "Rights": "Nothing" } } }""");

        var twin = Assert.Single(findings);
        Assert.Equal(ValidationCodes.DriveTwin, twin.Code);
        Assert.Equal(ValidationSeverity.Error, twin.Severity);
        Assert.Equal("Orkeon:Tools:Drive:Accounts:work:Provider", twin.Path);
    }

    [Fact]
    public void An_unreadable_screening_switch_is_said_for_the_section_and_for_each_account()
    {
        var findings = Validate("""
            { "Screening": { "WithholdRejected": "maybe" },
              "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } }
            """);

        Assert.Equal([ValidationCodes.DriveScreening, ValidationCodes.DriveScreening], findings.Select(finding => finding.Code));
        Assert.Equal("Orkeon:Tools:Drive:Screening:WithholdRejected", findings[0].Path);
        Assert.Equal("Orkeon:Tools:Drive:Accounts:work", findings[1].Path);
    }

    [Fact]
    public void The_email_findings_do_not_leak_into_the_drive_ones_nor_the_reverse()
    {
        var all = new AppSettingsValidator().ValidateJson("""
            { "Llm": { "Provider": "Ollama" }, "Orkeon": { "Tools": {
              "Email": { "Accounts": { "work": { "Provider": "Gmail" } } },
              "Drive": { "Accounts": { "work": { "Provider": "OneDrive", "Rights": "Read", "Auth": { "ClientId": "c" } } } } } } }
            """);

        Assert.DoesNotContain(all, message => message.Code.StartsWith("STUDIO-DRIVE-", StringComparison.Ordinal));
        Assert.Contains(all, message => message.Code.StartsWith("STUDIO-MAIL-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("perso", "DRIVE_PERSO_CLIENT_SECRET")]
    [InlineData("my.drive-2", "DRIVE_MY_DRIVE_2_CLIENT_SECRET")]
    public void The_variable_of_a_client_secret_is_derived_from_the_account_name(string account, string expected)
    {
        Assert.Equal(expected, DriveSecretNames.ClientSecretFor(account, []));
    }

    [Fact]
    public void A_derived_variable_stays_clear_of_the_names_already_taken_whatever_their_case()
    {
        Assert.Equal("DRIVE_A_B_CLIENT_SECRET_2", DriveSecretNames.ClientSecretFor("a-b", ["drive_a_b_client_secret"]));
        Assert.Equal("DRIVE_A_B_CLIENT_SECRET_3", DriveSecretNames.ClientSecretFor("a.b", ["DRIVE_A_B_CLIENT_SECRET", "DRIVE_A_B_CLIENT_SECRET_2", " "]));
        // Never ORKEON_: a run would read such a variable as configuration, its prefix removed.
        Assert.StartsWith("DRIVE_", DriveSecretNames.ClientSecretFor("orkeon", []), StringComparison.Ordinal);
    }
}
