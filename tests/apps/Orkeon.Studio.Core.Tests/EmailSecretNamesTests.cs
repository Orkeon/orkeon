using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-68 — the variable Studio keeps an account's secret in when the file names none: derived
/// from the account's name, free of what the other accounts of the file already name, and never a
/// name the run would read as configuration.
/// </summary>
public sealed class EmailSecretNamesTests
{
    [Fact]
    public void An_account_names_its_two_variables_after_itself()
    {
        Assert.Equal("EMAIL_PERSO_PASSWORD", EmailSecretNames.PasswordFor("perso", []));
        Assert.Equal("EMAIL_PERSO_CLIENT_SECRET", EmailSecretNames.ClientSecretFor("perso", []));
    }

    [Fact]
    public void Dots_and_dashes_become_underscores_and_the_second_account_of_the_same_spelling_takes_a_suffix()
    {
        var first = EmailSecretNames.PasswordFor("a.b", []);
        var second = EmailSecretNames.PasswordFor("a-b", [first]);
        var third = EmailSecretNames.PasswordFor("A_b", [first, second]);

        Assert.Equal("EMAIL_A_B_PASSWORD", first);
        Assert.Equal("EMAIL_A_B_PASSWORD_2", second);
        Assert.Equal("EMAIL_A_B_PASSWORD_3", third);
        Assert.Equal("EMAIL_A_B_CLIENT_SECRET_2", EmailSecretNames.ClientSecretFor("a-b", ["EMAIL_A_B_CLIENT_SECRET"]));
    }

    [Fact]
    public void A_name_another_account_holds_in_another_case_is_taken()
    {
        // Windows does not tell Email_Perso_Password from EMAIL_PERSO_PASSWORD: one variable.
        Assert.Equal("EMAIL_PERSO_PASSWORD_2", EmailSecretNames.PasswordFor("perso", ["Email_Perso_Password"]));
        Assert.Equal("EMAIL_PERSO_PASSWORD_3", EmailSecretNames.PasswordFor("perso", ["email_perso_password", " EMAIL_PERSO_PASSWORD_2 "]));
        // What the other accounts name otherwise is no obstacle.
        Assert.Equal("EMAIL_PERSO_PASSWORD", EmailSecretNames.PasswordFor("perso", ["GMAIL_APP_PASSWORD", "EMAIL_PERSO_CLIENT_SECRET", ""]));
    }

    [Theory]
    [InlineData("perso")]
    [InlineData("orkeon")]
    [InlineData("ORKEON_")]
    [InlineData("a.b-c_d")]
    [InlineData("9lives")]
    [InlineData("bo\u00eete perso")]
    [InlineData("a=b")]
    public void A_derived_name_is_a_variable_name_and_never_one_the_run_reads_as_configuration(string account)
    {
        foreach (var name in new[] { EmailSecretNames.PasswordFor(account, []), EmailSecretNames.ClientSecretFor(account, []) })
        {
            // ORKEON_* is read by the run as configuration, prefix removed: never a secret's name.
            Assert.StartsWith("EMAIL_", name, StringComparison.Ordinal);
            Assert.False(name.StartsWith("ORKEON_", StringComparison.OrdinalIgnoreCase));
            Assert.True(LlmSection.IsVariableName(name));
            Assert.Matches("^[A-Z0-9_]+$", name);
        }
    }
}
