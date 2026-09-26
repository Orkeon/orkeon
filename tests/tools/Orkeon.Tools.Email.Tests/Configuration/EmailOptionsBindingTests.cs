using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.DependencyInjection;

namespace Orkeon.Tools.Email.Tests.Configuration;

/// <summary>
/// The <c>Orkeon:Tools:Email</c> section binds as operators write it: flags as a comma list,
/// arrays, nested objects behind get-only properties, and account names in any case.
/// </summary>
public sealed class EmailOptionsBindingTests
{
    [Fact]
    public void Should_bind_the_section_as_an_operator_writes_it()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Email:DefaultAccount"] = "perso",
            ["Orkeon:Tools:Email:CredentialsDirectory"] = "/var/lib/orkeon/credentials",
            ["Orkeon:Tools:Email:Screening:WithholdRejected"] = "true",
            ["Orkeon:Tools:Email:Accounts:perso:Provider"] = "Gmail",
            ["Orkeon:Tools:Email:Accounts:perso:Address"] = "me@gmail.com",
            ["Orkeon:Tools:Email:Accounts:perso:Rights"] = "Read, Organize",
            ["Orkeon:Tools:Email:Accounts:perso:Auth:Method"] = "Password",
            ["Orkeon:Tools:Email:Accounts:perso:Auth:PasswordEnvVar"] = "ORKEON_EMAIL_PERSO_PASSWORD",
            ["Orkeon:Tools:Email:Accounts:perso:Send:AllowedRecipients:0"] = "boss@example.com",
            ["Orkeon:Tools:Email:Accounts:perso:Send:AllowedRecipients:1"] = "*@corp.example",
            ["Orkeon:Tools:Email:Accounts:perso:Send:MaxPerHour"] = "12",
            ["Orkeon:Tools:Email:Accounts:perso:Incoming:Protocol"] = "Pop3",
            ["Orkeon:Tools:Email:Accounts:perso:TimeoutSeconds"] = "30",
        });

        var options = provider.GetRequiredService<IOptions<EmailToolsOptions>>().Value;

        Assert.Equal("perso", options.DefaultAccount);
        Assert.Equal("/var/lib/orkeon/credentials", options.CredentialsDirectory);
        Assert.True(options.Screening.WithholdRejected);
        var perso = options.Accounts["PERSO"];
        Assert.Equal(EmailProvider.Gmail, perso.Provider);
        Assert.Equal(EmailRights.Read | EmailRights.Organize, perso.Rights);
        Assert.Equal(EmailAuthMethod.Password, perso.Auth.Method);
        Assert.Equal(["boss@example.com", "*@corp.example"], perso.Send.AllowedRecipients);
        Assert.Equal(12, perso.Send.MaxPerHour);
        Assert.Equal(IncomingProtocol.Pop3, perso.Incoming.Protocol);
        Assert.Equal(30, perso.TimeoutSeconds);
    }

    [Fact]
    public void Should_resolve_a_bound_account_whatever_the_case_of_its_name()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Email:Accounts:Perso:Provider"] = "Gmail",
            ["Orkeon:Tools:Email:Accounts:Perso:Address"] = "me@gmail.com",
            ["Orkeon:Tools:Email:Accounts:Perso:Rights"] = "Read",
            ["Orkeon:Tools:Email:Accounts:Perso:Auth:PasswordEnvVar"] = "ORKEON_EMAIL_PERSO_PASSWORD",
        });

        var account = provider.GetRequiredService<IEmailAccountRegistry>().Resolve("PERSO");

        Assert.Equal("Perso", account.Name);
        Assert.Equal("imap.gmail.com", account.IncomingEndpoint!.Host);
    }

    [Fact]
    public void Should_bind_nothing_and_throw_nothing_When_the_section_is_absent()
    {
        using var provider = Build(new Dictionary<string, string?>());

        var options = provider.GetRequiredService<IOptions<EmailToolsOptions>>().Value;

        Assert.Empty(options.Accounts);
        Assert.Null(options.DefaultAccount);
        Assert.False(options.Screening.WithholdRejected);
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonEmailTools(configuration);
        return services.BuildServiceProvider();
    }
}
