using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Configuration;

/// <summary>Every problem the resolver reports, each an actionable sentence naming the account.</summary>
public sealed class EmailAccountResolverValidationTests
{
    [Fact]
    public void Should_require_the_rights()
    {
        var problem = SingleProblem(TestAccounts.Custom(EmailRights.None));

        Assert.Equal("Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Organize, Draft\"", problem);
    }

    [Fact]
    public void Should_require_the_address()
    {
        var options = TestAccounts.Custom();
        options.Address = "  ";

        Assert.Equal("Address is required", SingleProblem(options));
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("agent.example.test")]
    public void Should_refuse_an_address_that_is_not_an_e_mail_address(string address)
    {
        var options = TestAccounts.Custom();
        options.Address = address;

        Assert.Equal($"Address '{address}' is not an e-mail address", SingleProblem(options));
    }

    [Fact]
    public void Should_refuse_a_password_for_Outlook()
    {
        var options = TestAccounts.Outlook();
        options.Auth.Method = EmailAuthMethod.Password;
        options.Auth.PasswordEnvVar = "SOME_VAR";

        Assert.Contains("Outlook.com and Microsoft 365 no longer accept passwords", SingleProblem(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_require_the_name_of_the_password_variable()
    {
        var options = TestAccounts.Custom();
        options.Auth.PasswordEnvVar = " ";

        Assert.Contains("Auth:PasswordEnvVar is required: the NAME of the environment variable", SingleProblem(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_require_a_client_id_for_OAuth2()
    {
        var options = TestAccounts.Gmail();
        options.Auth.Method = EmailAuthMethod.OAuth2;

        Assert.Contains("Auth:ClientId is required for OAuth2", SingleProblem(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_require_the_client_secret_variable_for_Gmail_OAuth2()
    {
        var options = TestAccounts.GmailOAuth();
        options.Auth.ClientSecretEnvVar = null;

        Assert.Contains("Auth:ClientSecretEnvVar is required for Gmail OAuth2", SingleProblem(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_require_the_incoming_host_of_a_custom_account()
    {
        var options = TestAccounts.Custom();
        options.Incoming.Host = null;

        Assert.Equal("Incoming:Host is required for a Custom account", SingleProblem(options));
    }

    [Fact]
    public void Should_refuse_OAuth2_on_a_custom_account()
    {
        var options = TestAccounts.Custom();
        options.Auth.Method = EmailAuthMethod.OAuth2;
        options.Auth.ClientId = "client";

        Assert.Contains("OAuth2 is available with the Gmail and Outlook presets", SingleProblem(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_refuse_Security_None_towards_a_remote_host_on_both_sides()
    {
        var options = TestAccounts.Custom(EmailRights.Send);
        options.Incoming.Security = TransportSecurity.None;
        options.Outgoing.Security = TransportSecurity.None;

        var problems = Problems(options);

        Assert.Equal(2, problems.Count);
        Assert.Contains("Incoming:Security None is only accepted towards a local test server (localhost); 'imap.example.test' needs SslOnConnect or StartTls", problems);
        Assert.Contains("Outgoing:Security None is only accepted towards a local test server (localhost); 'smtp.example.test' needs SslOnConnect or StartTls", problems);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Should_accept_Security_None_towards_a_loopback_test_server(string host)
    {
        var options = TestAccounts.Custom();
        options.Incoming.Host = host;
        options.Incoming.Port = 1143;
        options.Incoming.Security = TransportSecurity.None;

        var account = TestAccounts.Resolve("acct", options);

        Assert.Equal(new MailEndpoint(host, 1143, TransportSecurity.None), account.IncomingEndpoint);
    }

    [Fact]
    public void Should_offer_Graph_only_with_the_Outlook_preset()
    {
        var options = TestAccounts.Gmail();
        options.Incoming.Protocol = IncomingProtocol.Graph;

        Assert.Contains("Incoming:Protocol Graph is only available with the Outlook preset", Problems(options));
    }

    [Fact]
    public void Should_send_through_Graph_what_is_read_through_Graph()
    {
        var options = TestAccounts.Outlook(EmailRights.Send);
        options.Outgoing.Protocol = OutgoingProtocol.Smtp;

        Assert.Contains("an account read through Graph also sends through Graph", SingleProblem(options), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_refuse_Graph_sending_without_Graph_reading()
    {
        var options = TestAccounts.Gmail();
        options.Outgoing.Protocol = OutgoingProtocol.Graph;

        Assert.Equal("Outgoing:Protocol Graph needs Incoming:Protocol Graph", SingleProblem(options));
    }

    [Fact]
    public void Should_refuse_the_Send_right_without_an_outgoing_server()
    {
        var options = TestAccounts.Custom(EmailRights.Read | EmailRights.Send);
        options.Outgoing.Host = null;

        Assert.Equal("Rights grant Send but the account declares no outgoing server (Outgoing:Host)", SingleProblem(options));
    }

    [Theory]
    [InlineData("*@")]
    [InlineData("*@*.example.com")]
    [InlineData("*@a@b.com")]
    [InlineData("boss*@example.com")]
    [InlineData("Boss <boss@example.com>")]
    [InlineData("boss.example.com")]
    public void Should_refuse_an_allow_list_entry_that_is_not_an_address_a_domain_or_a_star(string pattern)
    {
        var options = TestAccounts.Custom(EmailRights.Send);
        options.Send.AllowedRecipients.Add(pattern);

        Assert.Equal($"Send:AllowedRecipients entry '{pattern}' is neither an address, '*@domain' nor '*'", SingleProblem(options));
    }

    [Theory]
    [InlineData(0, null, "Send:MaxRecipients must be positive")]
    [InlineData(null, -1, "Send:MaxPerHour must be positive")]
    public void Should_refuse_non_positive_send_limits(int? maxRecipients, int? maxPerHour, string expected)
    {
        var options = TestAccounts.Custom(EmailRights.Send);
        options.Send.MaxRecipients = maxRecipients;
        options.Send.MaxPerHour = maxPerHour;

        Assert.Equal($"{expected}", SingleProblem(options));
    }

    [Fact]
    public void Should_refuse_a_non_positive_timeout()
    {
        var options = TestAccounts.Custom();
        options.TimeoutSeconds = 0;

        Assert.Equal("TimeoutSeconds must be positive", SingleProblem(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Should_refuse_a_port_outside_the_TCP_range(int port)
    {
        var options = TestAccounts.Custom();
        options.Incoming.Port = port;

        Assert.Equal($"Incoming:Port {port} is not a TCP port", SingleProblem(options));
    }

    [Fact]
    public void Should_refuse_a_tenant_that_is_neither_an_alias_nor_an_id()
    {
        var options = TestAccounts.Outlook();
        options.Auth.Tenant = "contoso/../evil";

        Assert.Equal("Auth:Tenant 'contoso/../evil' is not a tenant alias or id", SingleProblem(options));
    }

    [Theory]
    [InlineData("my account")]
    [InlineData(".hidden")]
    [InlineData("-dash")]
    public void Should_refuse_an_invalid_account_name(string name)
    {
        var resolution = EmailAccountResolver.Resolve(name, TestAccounts.Custom());

        Assert.Null(resolution.Account);
        Assert.Equal(
            $"the account name may only hold letters, digits, '.', '_' and '-', starts with a letter or a digit, and has 64 characters at most",
            Assert.Single(resolution.Problems));
    }

    [Fact]
    public void Should_collect_every_problem_in_one_pass()
    {
        var options = new EmailAccountOptions { Provider = EmailProvider.Custom, Rights = EmailRights.Send };
        options.Send.MaxPerHour = 0;

        var problems = Problems(options);

        Assert.Contains("Address is required", problems);
        Assert.Contains("Incoming:Host is required for a Custom account", problems);
        Assert.Contains("Rights grant Send but the account declares no outgoing server (Outgoing:Host)", problems);
        Assert.Contains("Send:MaxPerHour must be positive", problems);
        Assert.Contains(problems, p => p.Contains("Auth:PasswordEnvVar is required", StringComparison.Ordinal));
        Assert.True(problems.Count >= 5);
    }

    private static List<string> Problems(EmailAccountOptions options)
    {
        var resolution = EmailAccountResolver.Resolve("acct", options);
        Assert.Null(resolution.Account);
        return [.. resolution.Problems];
    }

    private static string SingleProblem(EmailAccountOptions options) => Assert.Single(Problems(options));
}
