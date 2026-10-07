using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-65 — Studio Core re-spells the keys and the enumeration values of <c>Orkeon:Tools:Email</c>
/// instead of referencing <c>Orkeon.Tools.Email</c> (a form has no use for MailKit and MimeKit), so
/// the copies are held to the engine here, like <see cref="ConstantDriftTests"/> does for the
/// constants. The engine types are <c>internal</c>; the engine opens them to this project only.
/// </summary>
public sealed class EmailSectionDriftTests
{
    /// <summary>The options classes the binder fills, with the keys of <see cref="EmailSection.Keys"/> each one carries.</summary>
    private static readonly (Type Options, string[] Keys)[] Groups =
    [
        (typeof(EmailToolsOptions), [EmailSection.Keys.DefaultAccount, EmailSection.Keys.CredentialsDirectory, EmailSection.Keys.Accounts, EmailSection.Keys.Screening]),
        (typeof(EmailScreeningOptions), [EmailSection.Keys.WithholdRejected]),
        (typeof(EmailAccountOptions),
        [
            EmailSection.Keys.Provider, EmailSection.Keys.Address, EmailSection.Keys.DisplayName, EmailSection.Keys.Rights,
            EmailSection.Keys.Incoming, EmailSection.Keys.Outgoing, EmailSection.Keys.Auth, EmailSection.Keys.Send,
            EmailSection.Keys.TimeoutSeconds, EmailSection.Keys.SaveSentCopy,
        ]),
        (typeof(EmailIncomingOptions), [EmailSection.Keys.Protocol, EmailSection.Keys.Host, EmailSection.Keys.Port, EmailSection.Keys.Security]),
        (typeof(EmailOutgoingOptions), [EmailSection.Keys.Protocol, EmailSection.Keys.Host, EmailSection.Keys.Port, EmailSection.Keys.Security]),
        (typeof(EmailAuthOptions),
        [
            EmailSection.Keys.Method, EmailSection.Keys.Username, EmailSection.Keys.PasswordEnvVar,
            EmailSection.Keys.ClientId, EmailSection.Keys.ClientSecretEnvVar, EmailSection.Keys.Tenant,
        ]),
        (typeof(EmailSendOptions), [EmailSection.Keys.AllowedRecipients, EmailSection.Keys.MaxRecipients, EmailSection.Keys.MaxPerHour]),
    ];

    [Fact]
    public void The_section_path_is_the_one_the_runner_binds()
    {
        Assert.Equal(ConfigurationKeys.ToolsEmail, EmailSection.SectionPath);
        Assert.Equal(ConfigurationKeys.ToolsEmail + ":" + EmailSection.Keys.Accounts, EmailSection.AccountsPath);
    }

    [Fact]
    public void Every_public_property_of_an_options_class_has_its_key_and_every_key_is_a_property()
    {
        foreach (var (options, keys) in Groups)
        {
            var properties = options
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal);

            Assert.True(
                properties.SequenceEqual(keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                $"{options.Name} binds [{string.Join(", ", properties)}] but EmailSection.Keys models [{string.Join(", ", keys.Order(StringComparer.Ordinal))}] for it.");
        }
    }

    [Fact]
    public void Every_key_constant_belongs_to_an_options_class()
    {
        var constants = typeof(EmailSection.Keys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.Equal(
            Groups.SelectMany(group => group.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            constants.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_spelling_of_an_enumeration_is_a_member_of_the_engine_enumeration_and_the_reverse()
    {
        Assert.Equal(NamesOf<EmailProvider>(), EmailSection.Providers.Order(StringComparer.Ordinal));
        Assert.Equal(NamesOf<IncomingProtocol>(), EmailSection.IncomingProtocols.Order(StringComparer.Ordinal));
        Assert.Equal(NamesOf<OutgoingProtocol>(), EmailSection.OutgoingProtocols.Order(StringComparer.Ordinal));
        Assert.Equal(NamesOf<TransportSecurity>(), EmailSection.Securities.Order(StringComparer.Ordinal));
        Assert.Equal(NamesOf<EmailAuthMethod>(), EmailSection.AuthMethods.Order(StringComparer.Ordinal));
        Assert.Equal(
            NamesOf<EmailRights>().Where(name => name != nameof(EmailRights.None)),
            EmailSection.Rights.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_engine_lists_its_rights_in_the_order_studio_writes_them_and_with_the_same_values()
    {
        Assert.Equal(
            Enum.GetValues<EmailRights>().Where(right => right != EmailRights.None).Select(right => right.ToString()),
            EmailSection.Rights);

        foreach (var right in Enum.GetValues<EmailRights>())
            Assert.Equal((int)right, (int)Enum.Parse<EmailRight>(right.ToString()));
        Assert.Equal(Enum.GetNames<EmailRights>().Order(StringComparer.Ordinal), Enum.GetNames<EmailRight>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_account_filled_field_by_field_is_bound_by_the_engine_with_no_problem_and_the_values_written()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.DefaultAccount = "work";
        document.Email.WithholdRejected = true;
        document.Email.CredentialsDirectory = "/srv/orkeon/credentials";
        document.Email.SetAccount(new EmailAccountDefinition
        {
            Name = "work",
            Provider = "Outlook",
            Address = "me@example.com",
            DisplayName = "Me",
            Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft | EmailRight.Send | EmailRight.Delete | EmailRight.Purge,
            TimeoutSeconds = 30,
            SaveSentCopy = false,
            IncomingProtocol = "Pop3",
            IncomingHost = "pop.example.com",
            IncomingPort = 995,
            IncomingSecurity = "SslOnConnect",
            OutgoingProtocol = "Smtp",
            OutgoingHost = "smtp.example.com",
            OutgoingPort = 587,
            OutgoingSecurity = "StartTls",
            AuthMethod = "OAuth2",
            Username = "me",
            PasswordEnvVar = "WORK_MAIL_PASSWORD",
            ClientId = "client-id",
            ClientSecretEnvVar = "CLIENT_SECRET",
            Tenant = "organizations",
            AllowedRecipients = ["*@example.com", "you@gmail.com"],
            MaxRecipients = 5,
            MaxPerHour = 20,
        });

        var options = Bind(document);

        Assert.Empty(options.SectionProblems);
        Assert.Empty(options.AccountProblems);
        Assert.Equal("work", options.DefaultAccount);
        Assert.True(options.Screening.WithholdRejected);
        Assert.Equal("/srv/orkeon/credentials", options.CredentialsDirectory);

        var account = Assert.Single(options.Accounts).Value;
        Assert.Equal(EmailProvider.Outlook, account.Provider);
        Assert.Equal("me@example.com", account.Address);
        Assert.Equal("Me", account.DisplayName);
        Assert.Equal(
            EmailRights.Read | EmailRights.Organize | EmailRights.Draft | EmailRights.Send | EmailRights.Delete | EmailRights.Purge,
            account.Rights);
        Assert.Equal(30, account.TimeoutSeconds);
        Assert.False(account.SaveSentCopy);
        Assert.Equal(IncomingProtocol.Pop3, account.Incoming.Protocol);
        Assert.Equal("pop.example.com", account.Incoming.Host);
        Assert.Equal(995, account.Incoming.Port);
        Assert.Equal(TransportSecurity.SslOnConnect, account.Incoming.Security);
        Assert.Equal(OutgoingProtocol.Smtp, account.Outgoing.Protocol);
        Assert.Equal("smtp.example.com", account.Outgoing.Host);
        Assert.Equal(587, account.Outgoing.Port);
        Assert.Equal(TransportSecurity.StartTls, account.Outgoing.Security);
        Assert.Equal(EmailAuthMethod.OAuth2, account.Auth.Method);
        Assert.Equal("me", account.Auth.Username);
        Assert.Equal("WORK_MAIL_PASSWORD", account.Auth.PasswordEnvVar);
        Assert.Equal("client-id", account.Auth.ClientId);
        Assert.Equal("CLIENT_SECRET", account.Auth.ClientSecretEnvVar);
        Assert.Equal("organizations", account.Auth.Tenant);
        Assert.Equal(["*@example.com", "you@gmail.com"], account.Send.AllowedRecipients);
        Assert.Equal(5, account.Send.MaxRecipients);
        Assert.Equal(20, account.Send.MaxPerHour);
    }

    [Fact]
    public void Every_spelling_studio_offers_is_one_the_engine_binds_to_that_same_member()
    {
        foreach (var provider in EmailSection.Providers)
            Assert.Equal(provider, BindOne(new EmailAccountDefinition { Name = "a", Provider = provider }).Provider.ToString());
        foreach (var protocol in EmailSection.IncomingProtocols)
            Assert.Equal(protocol, BindOne(new EmailAccountDefinition { Name = "a", IncomingProtocol = protocol }).Incoming.Protocol.ToString());
        foreach (var protocol in EmailSection.OutgoingProtocols)
            Assert.Equal(protocol, BindOne(new EmailAccountDefinition { Name = "a", OutgoingProtocol = protocol }).Outgoing.Protocol.ToString());
        foreach (var security in EmailSection.Securities)
        {
            Assert.Equal(security, BindOne(new EmailAccountDefinition { Name = "a", IncomingSecurity = security }).Incoming.Security.ToString());
            Assert.Equal(security, BindOne(new EmailAccountDefinition { Name = "a", OutgoingSecurity = security }).Outgoing.Security.ToString());
        }

        foreach (var method in EmailSection.AuthMethods)
            Assert.Equal(method, BindOne(new EmailAccountDefinition { Name = "a", AuthMethod = method }).Auth.Method.ToString());
    }

    [Fact]
    public void Each_right_studio_writes_alone_and_all_together_are_bound_by_the_engine_as_those_rights()
    {
        foreach (var right in Enum.GetValues<EmailRight>().Where(right => right != EmailRight.None))
        {
            var bound = BindOne(new EmailAccountDefinition { Name = "a", Rights = right }).Rights;
            Assert.Equal(right.ToString(), bound.ToString());
        }
    }

    [Fact]
    public void A_coherent_account_written_by_studio_is_resolved_by_the_engine_with_no_problem()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(new EmailAccountDefinition
        {
            Name = "work",
            Provider = "Custom",
            Address = "me@example.com",
            Rights = EmailRight.Read | EmailRight.Organize | EmailRight.Draft | EmailRight.Send,
            IncomingProtocol = "Imap",
            IncomingHost = "imap.example.com",
            OutgoingHost = "smtp.example.com",
            OutgoingPort = 587,
            OutgoingSecurity = "StartTls",
            AuthMethod = "Password",
            PasswordEnvVar = "WORK_MAIL_PASSWORD",
            AllowedRecipients = ["*@example.com"],
            MaxPerHour = 20,
        });

        var options = Bind(document);
        var resolution = EmailAccountResolver.Resolve("work", options.Accounts["work"]);

        Assert.Empty(options.AccountProblems);
        Assert.Empty(resolution.Problems);
        Assert.NotNull(resolution.Account);
    }

    private static EmailAccountOptions BindOne(EmailAccountDefinition account)
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Email.SetAccount(account);

        var options = Bind(document);

        Assert.Empty(options.AccountProblems);
        return options.Accounts[account.Name];
    }

    private static EmailToolsOptions Bind(AppSettingsDocument document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJson()));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new EmailToolsOptions();

        EmailOptionsBinder.Bind(configuration.GetSection(ConfigurationKeys.ToolsEmail), options);
        return options;
    }

    private static IEnumerable<string> NamesOf<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Order(StringComparer.Ordinal);
}
