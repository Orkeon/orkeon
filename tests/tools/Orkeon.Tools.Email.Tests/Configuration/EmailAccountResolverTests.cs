using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Configuration;

/// <summary>The provider presets and what a resolved account carries.</summary>
public sealed class EmailAccountResolverTests
{
    [Fact]
    public void Should_fill_the_Gmail_IMAP_and_SMTP_servers_over_implicit_TLS()
    {
        var account = TestAccounts.Resolve("perso", TestAccounts.Gmail(EmailRights.Read | EmailRights.Send));

        Assert.Equal(EmailProvider.Gmail, account.Provider);
        Assert.Equal(IncomingProtocol.Imap, account.Incoming);
        Assert.Equal(new MailEndpoint("imap.gmail.com", 993, TransportSecurity.SslOnConnect), account.IncomingEndpoint);
        Assert.Equal(OutgoingProtocol.Smtp, account.Outgoing);
        Assert.Equal(new MailEndpoint("smtp.gmail.com", 465, TransportSecurity.SslOnConnect), account.OutgoingEndpoint);
        Assert.Equal(EmailAuthMethod.Password, account.Auth.Method);
        Assert.Equal("someone@gmail.com", account.Auth.Username);
        Assert.Equal(TestAccounts.PasswordVariable, account.Auth.PasswordEnvVar);
        Assert.Null(account.Auth.OAuth);
        Assert.False(account.SaveSentCopy);
    }

    [Fact]
    public void Should_fill_the_Gmail_POP3_server_When_the_account_reads_over_POP3()
    {
        var options = TestAccounts.Gmail();
        options.Incoming.Protocol = IncomingProtocol.Pop3;

        var account = TestAccounts.Resolve("perso", options);

        Assert.Equal(new MailEndpoint("pop.gmail.com", 995, TransportSecurity.SslOnConnect), account.IncomingEndpoint);
    }

    [Fact]
    public void Should_sign_Gmail_in_with_the_loopback_PKCE_flow_and_the_mail_scope()
    {
        var account = TestAccounts.Resolve("perso", TestAccounts.GmailOAuth());

        var oauth = Assert.IsType<OAuthSettings>(account.Auth.OAuth);
        Assert.Equal(EmailAuthMethod.OAuth2, account.Auth.Method);
        Assert.Equal(OAuthFlow.LoopbackPkce, oauth.Flow);
        Assert.Equal("google-client.apps.googleusercontent.com", oauth.ClientId);
        Assert.Equal("ORKEON_TEST_GOOGLE_SECRET", oauth.ClientSecretEnvVar);
        Assert.Equal(new Uri("https://oauth2.googleapis.com/token"), oauth.TokenEndpoint);
        Assert.Equal(new Uri("https://accounts.google.com/o/oauth2/v2/auth"), oauth.AuthorizationEndpoint);
        Assert.Null(oauth.DeviceCodeEndpoint);
        Assert.Equal(["https://mail.google.com/"], oauth.Scopes);
        Assert.False(oauth.ScopesOnRefresh);
    }

    [Fact]
    public void Should_read_and_send_Outlook_through_Graph_with_the_device_code_flow_on_the_consumers_tenant()
    {
        var account = TestAccounts.Resolve("hotmail", TestAccounts.Outlook(EmailRights.Read | EmailRights.Send));

        Assert.Equal(IncomingProtocol.Graph, account.Incoming);
        Assert.Null(account.IncomingEndpoint);
        Assert.Equal(OutgoingProtocol.Graph, account.Outgoing);
        Assert.Null(account.OutgoingEndpoint);
        Assert.Equal(EmailAuthMethod.OAuth2, account.Auth.Method);
        var oauth = Assert.IsType<OAuthSettings>(account.Auth.OAuth);
        Assert.Equal(OAuthFlow.DeviceCode, oauth.Flow);
        Assert.Equal(new Uri("https://login.microsoftonline.com/consumers/oauth2/v2.0/token"), oauth.TokenEndpoint);
        Assert.Equal(new Uri("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode"), oauth.DeviceCodeEndpoint);
        Assert.Equal(new Uri("https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize"), oauth.AuthorizationEndpoint);
        Assert.Equal(["https://graph.microsoft.com/Mail.ReadWrite", "https://graph.microsoft.com/Mail.Send", "offline_access"], oauth.Scopes);
        Assert.True(oauth.ScopesOnRefresh);
        Assert.False(account.SaveSentCopy);
    }

    [Fact]
    public void Should_use_the_declared_Microsoft_tenant()
    {
        var options = TestAccounts.Outlook();
        options.Auth.Tenant = " organizations ";

        var account = TestAccounts.Resolve("work", options);

        Assert.Equal(new Uri("https://login.microsoftonline.com/organizations/oauth2/v2.0/token"), account.Auth.OAuth!.TokenEndpoint);
    }

    [Fact]
    public void Should_fall_back_to_the_Outlook_IMAP_and_SMTP_servers_with_their_scopes()
    {
        var options = TestAccounts.Outlook(EmailRights.Read | EmailRights.Send);
        options.Incoming.Protocol = IncomingProtocol.Imap;

        var account = TestAccounts.Resolve("hotmail", options);

        Assert.Equal(new MailEndpoint("outlook.office365.com", 993, TransportSecurity.SslOnConnect), account.IncomingEndpoint);
        Assert.Equal(new MailEndpoint("smtp-mail.outlook.com", 587, TransportSecurity.StartTls), account.OutgoingEndpoint);
        Assert.Equal(
            ["https://outlook.office.com/IMAP.AccessAsUser.All", "https://outlook.office.com/SMTP.Send", "offline_access"],
            account.Auth.OAuth!.Scopes);
    }

    [Fact]
    public void Should_ask_for_the_POP_scope_When_an_Outlook_account_reads_over_POP3()
    {
        var options = TestAccounts.Outlook();
        options.Incoming.Protocol = IncomingProtocol.Pop3;

        var account = TestAccounts.Resolve("hotmail", options);

        Assert.Equal(new MailEndpoint("outlook.office365.com", 995, TransportSecurity.SslOnConnect), account.IncomingEndpoint);
        Assert.Contains("https://outlook.office.com/POP.AccessAsUser.All", account.Auth.OAuth!.Scopes);
    }

    [Theory]
    [InlineData("Imap", "SslOnConnect", 993)]
    [InlineData("Imap", "StartTls", 143)]
    [InlineData("Pop3", "SslOnConnect", 995)]
    [InlineData("Pop3", "StartTls", 110)]
    public void Should_pick_the_conventional_incoming_port_of_the_protocol_and_security(string protocol, string security, int port)
    {
        var options = TestAccounts.Custom();
        options.Incoming.Protocol = Enum.Parse<IncomingProtocol>(protocol);
        options.Incoming.Security = Enum.Parse<TransportSecurity>(security);

        var account = TestAccounts.Resolve("custom", options);

        Assert.Equal(port, account.IncomingEndpoint!.Port);
    }

    [Theory]
    [InlineData("SslOnConnect", 465)]
    [InlineData("StartTls", 587)]
    public void Should_pick_the_conventional_submission_port_of_the_security(string security, int port)
    {
        var options = TestAccounts.Custom(EmailRights.Send);
        options.Outgoing.Security = Enum.Parse<TransportSecurity>(security);

        var account = TestAccounts.Resolve("custom", options);

        Assert.Equal(new MailEndpoint("smtp.example.test", port, Enum.Parse<TransportSecurity>(security)), account.OutgoingEndpoint);
    }

    [Fact]
    public void Should_keep_explicit_hosts_ports_and_security_over_the_preset()
    {
        var options = TestAccounts.Gmail(EmailRights.Send);
        options.Incoming.Host = " mail.example.org ";
        options.Incoming.Port = 1993;
        options.Outgoing.Host = "relay.example.org";
        options.Outgoing.Port = 2525;
        options.Outgoing.Security = TransportSecurity.StartTls;

        var account = TestAccounts.Resolve("perso", options);

        Assert.Equal(new MailEndpoint("mail.example.org", 1993, TransportSecurity.SslOnConnect), account.IncomingEndpoint);
        Assert.Equal(new MailEndpoint("relay.example.org", 2525, TransportSecurity.StartTls), account.OutgoingEndpoint);
    }

    [Fact]
    public void Should_file_sent_copies_itself_only_for_a_custom_SMTP_account_unless_told_otherwise()
    {
        var custom = TestAccounts.Resolve("custom", TestAccounts.Custom(EmailRights.Send));
        var readOnly = TestAccounts.Custom();
        readOnly.Outgoing.Host = null;
        var withoutOutgoing = TestAccounts.Resolve("reader", readOnly);
        var gmail = TestAccounts.Gmail(EmailRights.Send);
        gmail.SaveSentCopy = true;

        Assert.True(custom.SaveSentCopy);
        Assert.False(withoutOutgoing.SaveSentCopy);
        Assert.Null(withoutOutgoing.Outgoing);
        Assert.Null(withoutOutgoing.OutgoingEndpoint);
        Assert.True(TestAccounts.Resolve("perso", gmail).SaveSentCopy);
    }

    [Fact]
    public void Should_file_no_sent_copy_by_default_for_a_POP3_account_which_has_no_Sent_folder()
    {
        var pop = TestAccounts.Custom(EmailRights.Send);
        pop.Incoming.Protocol = IncomingProtocol.Pop3;

        Assert.False(TestAccounts.Resolve("pop", pop).SaveSentCopy);
    }

    [Fact]
    public void Should_normalize_the_address_the_user_name_the_display_name_and_the_allow_list()
    {
        var options = TestAccounts.Custom(EmailRights.Send);
        options.Address = " Agent Smith <Agent@Example.test> ";
        options.DisplayName = "  Support desk ";
        options.Auth.Username = "  login-name ";
        options.Send.AllowedRecipients.AddRange([" boss@example.com ", "", "   ", "*@partner.example"]);
        options.Send.MaxRecipients = 5;
        options.Send.MaxPerHour = 20;
        options.TimeoutSeconds = 45;

        var account = TestAccounts.Resolve("custom", options);

        Assert.Equal("Agent@Example.test", account.Address);
        Assert.Equal("Support desk", account.DisplayName);
        Assert.Equal("login-name", account.Auth.Username);
        Assert.Equal(["boss@example.com", "*@partner.example"], account.Send.AllowedRecipients);
        Assert.Equal(5, account.Send.MaxRecipients);
        Assert.Equal(20, account.Send.MaxPerHour);
        Assert.Equal(TimeSpan.FromSeconds(45), account.Timeout);
    }

    [Fact]
    public void Should_leave_the_display_name_and_timeout_unset_When_not_declared()
    {
        var options = TestAccounts.Custom();
        options.DisplayName = "   ";

        var account = TestAccounts.Resolve("custom", options);

        Assert.Null(account.DisplayName);
        Assert.Null(account.Timeout);
        Assert.Empty(account.Send.AllowedRecipients);
    }

    [Theory]
    [InlineData("Read", "Read", true)]
    [InlineData("Read, Organize", "Organize", true)]
    [InlineData("Read, Organize", "Read, Organize", true)]
    [InlineData("Read", "Read, Organize", false)]
    [InlineData("Delete", "Purge", false)]
    public void Should_grant_exactly_the_declared_rights(string declared, string asked, bool granted)
    {
        var account = TestAccounts.Resolve("custom", TestAccounts.Custom(Enum.Parse<EmailRights>(declared)));

        Assert.Equal(granted, account.Grants(Enum.Parse<EmailRights>(asked)));
    }

    [Theory]
    [InlineData(null, null, "Custom", "Password")]
    [InlineData(null, "client-id", "Custom", "OAuth2")]
    [InlineData(null, null, "Outlook", "OAuth2")]
    [InlineData("Password", "client-id", "Gmail", "Password")]
    [InlineData("OAuth2", null, "Gmail", "OAuth2")]
    public void Should_infer_the_sign_in_method_from_the_client_id_or_the_Outlook_preset(string? method, string? clientId, string provider, string expected)
    {
        var options = new EmailAccountOptions
        {
            Provider = Enum.Parse<EmailProvider>(provider),
            Auth = { Method = method is null ? null : Enum.Parse<EmailAuthMethod>(method), ClientId = clientId },
        };

        Assert.Equal(Enum.Parse<EmailAuthMethod>(expected), EmailAccountResolver.EffectiveAuthMethod(options));
    }

    [Theory]
    [InlineData("perso", true)]
    [InlineData("work.mail_2-b", true)]
    [InlineData("", false)]
    [InlineData("my account", false)]
    [InlineData("a/b", false)]
    [InlineData("été", false)]
    public void Should_accept_only_letters_digits_dots_underscores_and_dashes_as_account_names(string name, bool valid)
    {
        Assert.Equal(valid, EmailAccountResolver.IsValidName(name));
    }

    [Fact]
    public void Should_refuse_an_account_name_longer_than_64_characters()
    {
        Assert.True(EmailAccountResolver.IsValidName(new string('a', 64)));
        Assert.False(EmailAccountResolver.IsValidName(new string('a', 65)));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.8.9.10", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("imap.example.test", false)]
    [InlineData("10.0.0.1", false)]
    public void Should_recognize_loopback_hosts(string host, bool loopback)
    {
        Assert.Equal(loopback, EmailAccountResolver.IsLoopbackHost(host));
    }
}
