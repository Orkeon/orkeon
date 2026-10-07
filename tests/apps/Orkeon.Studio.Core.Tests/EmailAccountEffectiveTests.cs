using System.Text;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-66 — what a field left blank is worth. The form shows it without writing it (port 993,
/// <c>imap.gmail.com</c>, OAuth2 for Outlook), so <see cref="EmailAccountEffective"/> spells the
/// presets of <c>Orkeon.Tools.Email</c> again, and each of them is held here to the account the
/// engine's resolver returns for the same file.
/// </summary>
public sealed class EmailAccountEffectiveTests
{
    private const EmailRight Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft;

    private static readonly EmailAccountDefinition Gmail = new()
    {
        Name = "a", Provider = "Gmail", Address = "me@gmail.com", Rights = Rights, PasswordEnvVar = "GMAIL_APP_PASSWORD",
    };

    private static readonly EmailAccountDefinition Outlook = new()
    {
        Name = "a", Provider = "Outlook", Address = "me@hotmail.com", Rights = Rights, ClientId = "client",
    };

    private static readonly EmailAccountDefinition Custom = new()
    {
        Name = "a", Address = "me@example.com", Rights = Rights, IncomingHost = "imap.example.com", OutgoingHost = "smtp.example.com", PasswordEnvVar = "P",
    };

    [Fact]
    public void A_gmail_account_left_blank_reads_imap_and_sends_smtp_over_tls_with_a_password()
    {
        var effective = EmailAccountEffective.Of(Gmail);

        Assert.Equal("Imap", effective.IncomingProtocol);
        Assert.Equal("imap.gmail.com", effective.IncomingHost);
        Assert.Equal(993, effective.IncomingPort);
        Assert.Equal("SslOnConnect", effective.IncomingSecurity);
        Assert.Equal("Smtp", effective.OutgoingProtocol);
        Assert.Equal("smtp.gmail.com", effective.OutgoingHost);
        Assert.Equal(465, effective.OutgoingPort);
        Assert.Equal("SslOnConnect", effective.OutgoingSecurity);
        Assert.Equal("Password", effective.AuthMethod);
        Assert.Equal("me@gmail.com", effective.Username);
        Assert.Null(effective.Tenant);
        Assert.False(effective.SaveSentCopy);
        Assert.True(effective.CanSend);
    }

    [Fact]
    public void An_outlook_account_left_blank_goes_through_graph_with_oauth2_on_the_consumers_tenant()
    {
        var effective = EmailAccountEffective.Of(Outlook);

        Assert.Equal("Graph", effective.IncomingProtocol);
        Assert.Null(effective.IncomingHost);
        Assert.Null(effective.IncomingPort);
        Assert.Null(effective.IncomingSecurity);
        Assert.Equal("Graph", effective.OutgoingProtocol);
        Assert.Null(effective.OutgoingHost);
        Assert.Null(effective.OutgoingPort);
        Assert.Null(effective.OutgoingSecurity);
        Assert.Equal("OAuth2", effective.AuthMethod);
        Assert.Equal("consumers", effective.Tenant);
        Assert.False(effective.SaveSentCopy);
        Assert.True(effective.CanSend);
    }

    [Fact]
    public void An_outlook_account_read_through_imap_sends_through_its_smtp_server_with_starttls()
    {
        var effective = EmailAccountEffective.Of(Outlook with { IncomingProtocol = "Imap" });

        Assert.Equal("outlook.office365.com", effective.IncomingHost);
        Assert.Equal(993, effective.IncomingPort);
        Assert.Equal("Smtp", effective.OutgoingProtocol);
        Assert.Equal("smtp-mail.outlook.com", effective.OutgoingHost);
        Assert.Equal(587, effective.OutgoingPort);
        Assert.Equal("StartTls", effective.OutgoingSecurity);
    }

    [Fact]
    public void A_custom_account_has_no_host_of_its_own_and_cannot_send_until_it_names_an_outgoing_server()
    {
        var blank = EmailAccountEffective.Of(new EmailAccountDefinition { Name = "a" });
        var complete = EmailAccountEffective.Of(Custom);

        Assert.Equal("Imap", blank.IncomingProtocol);
        Assert.Null(blank.IncomingHost);
        Assert.Equal(993, blank.IncomingPort);
        Assert.Equal("SslOnConnect", blank.IncomingSecurity);
        Assert.Equal("Smtp", blank.OutgoingProtocol);
        Assert.Null(blank.OutgoingHost);
        Assert.Equal(465, blank.OutgoingPort);
        Assert.Equal("SslOnConnect", blank.OutgoingSecurity);
        Assert.Equal("Password", blank.AuthMethod);
        Assert.Null(blank.Username);
        Assert.False(blank.CanSend);
        Assert.False(blank.SaveSentCopy);

        Assert.True(complete.CanSend);
        Assert.True(complete.SaveSentCopy);
        Assert.False(EmailAccountEffective.Of(Custom with { IncomingProtocol = "Pop3" }).SaveSentCopy);
    }

    [Fact]
    public void A_port_follows_the_security_of_its_side_and_a_method_the_client_id()
    {
        Assert.Equal(143, EmailAccountEffective.Of(Gmail with { IncomingSecurity = "StartTls" }).IncomingPort);
        Assert.Equal(995, EmailAccountEffective.Of(Gmail with { IncomingProtocol = "Pop3" }).IncomingPort);
        Assert.Equal("pop.gmail.com", EmailAccountEffective.Of(Gmail with { IncomingProtocol = "Pop3" }).IncomingHost);
        Assert.Equal(110, EmailAccountEffective.Of(Gmail with { IncomingProtocol = "Pop3", IncomingSecurity = "None" }).IncomingPort);
        Assert.Equal(587, EmailAccountEffective.Of(Gmail with { OutgoingSecurity = "StartTls" }).OutgoingPort);
        Assert.Equal("OAuth2", EmailAccountEffective.Of(Gmail with { ClientId = "client" }).AuthMethod);
        Assert.Equal("Password", EmailAccountEffective.Of(Gmail with { ClientId = "client", AuthMethod = "Password" }).AuthMethod);
    }

    [Fact]
    public void A_value_the_file_holds_wins_over_the_preset_and_a_value_the_engine_cannot_read_leaves_the_preset()
    {
        var declared = EmailAccountEffective.Of(Gmail with
        {
            IncomingHost = " imap.example.com ", IncomingPort = 1993, OutgoingHost = "smtp.example.com", OutgoingPort = 2525,
            Username = " me ", SaveSentCopy = true,
        });
        var unreadable = EmailAccountEffective.Of(Gmail with { Provider = "Yahoo", IncomingProtocol = "Imap4", IncomingSecurity = "Tls", AuthMethod = "Kerberos" });

        Assert.Equal("imap.example.com", declared.IncomingHost);
        Assert.Equal(1993, declared.IncomingPort);
        Assert.Equal("smtp.example.com", declared.OutgoingHost);
        Assert.Equal(2525, declared.OutgoingPort);
        Assert.Equal("me", declared.Username);
        Assert.True(declared.SaveSentCopy);

        Assert.Equal("Imap", unreadable.IncomingProtocol);
        Assert.Null(unreadable.IncomingHost);
        Assert.Equal("SslOnConnect", unreadable.IncomingSecurity);
        Assert.Equal("Password", unreadable.AuthMethod);
    }

    /// <summary>
    /// Every preset, every way to read and every security the engine resolves, one field declared
    /// at a time and the others left blank.
    /// </summary>
    public static TheoryData<string, string?, string?, string?> Presets()
    {
        var data = new TheoryData<string, string?, string?, string?>();
        foreach (var provider in EmailSection.Providers)
        {
            foreach (var protocol in new[] { null, "Imap", "Pop3" })
            {
                foreach (var incoming in new[] { null, "SslOnConnect", "StartTls" })
                {
                    foreach (var outgoing in new[] { null, "SslOnConnect", "StartTls" })
                        data.Add(provider, protocol, incoming, outgoing);
                }
            }
        }

        data.Add("Outlook", "Graph", null, null);
        return data;
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void Every_field_left_blank_is_worth_what_the_engine_resolves(string provider, string? protocol, string? incomingSecurity, string? outgoingSecurity)
    {
        var definition = (provider switch { "Gmail" => Gmail, "Outlook" => Outlook, _ => Custom }) with
        {
            IncomingProtocol = protocol,
            IncomingSecurity = incomingSecurity,
            OutgoingSecurity = outgoingSecurity,
        };

        var account = Resolve(definition);

        AssertSameAsTheEngine(account, EmailAccountEffective.Of(definition));
    }

    /// <summary>Each member of <paramref name="effective"/> against the account the engine resolved.</summary>
    internal static void AssertSameAsTheEngine(ResolvedEmailAccount account, EmailAccountEffective effective)
    {
        Assert.Equal(account.Incoming.ToString(), effective.IncomingProtocol);
        Assert.Equal(account.IncomingEndpoint?.Host, effective.IncomingHost);
        Assert.Equal(account.IncomingEndpoint?.Port, effective.IncomingPort);
        Assert.Equal(account.IncomingEndpoint?.Security.ToString(), effective.IncomingSecurity);

        // An account without an outgoing server has no sending side to the engine; the port and the
        // security Studio still shows there are what the side is worth once a host is named.
        Assert.Equal(account.Outgoing is not null, effective.CanSend);
        Assert.Equal(account.Outgoing?.ToString(), effective.CanSend ? effective.OutgoingProtocol : null);
        Assert.Equal(account.OutgoingEndpoint?.Host, effective.OutgoingHost);
        if (account.OutgoingEndpoint is { } outgoing)
        {
            Assert.Equal(outgoing.Port, effective.OutgoingPort);
            Assert.Equal(outgoing.Security.ToString(), effective.OutgoingSecurity);
        }
        else if (account.Outgoing == OutgoingProtocol.Graph)
        {
            Assert.Null(effective.OutgoingPort);
            Assert.Null(effective.OutgoingSecurity);
        }

        Assert.Equal(account.Auth.Method.ToString(), effective.AuthMethod);
        Assert.Equal(account.Auth.Username, effective.Username);
        Assert.Equal(account.SaveSentCopy, effective.SaveSentCopy);

        // The engine keeps the tenant in the endpoints it derives from it.
        if (account.Provider == EmailProvider.Outlook && account.Auth.OAuth is { } oauth)
            Assert.Equal($"/{effective.Tenant}/oauth2/v2.0/token", oauth.TokenEndpoint.AbsolutePath);
        else
            Assert.Null(effective.Tenant);
    }

    private static ResolvedEmailAccount Resolve(EmailAccountDefinition definition)
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(definition);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJson()));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new EmailToolsOptions();
        EmailOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsEmail), options);

        var resolution = EmailAccountResolver.Resolve(definition.Name, options.Accounts[definition.Name]);
        Assert.Empty(options.AccountProblems);
        Assert.Empty(resolution.Problems);
        return resolution.Account!;
    }
}
