using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.DependencyInjection;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Tests.Doubles;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.DependencyInjection;

/// <summary>The family registers inert with no configuration, once, and lets a host bring its token store.</summary>
public sealed class EmailToolsServiceCollectionExtensionsTests
{
    private static readonly string[] ToolNames =
    [
        "email_accounts", "email_create_folder", "email_delete", "email_draft", "email_folders", "email_mark", "email_move",
        "email_parser", "email_read", "email_rename_folder", "email_save_attachment", "email_search", "email_send",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Should_register_the_thirteen_tools_with_no_e_mail_configuration_at_all()
    {
        using var provider = Build(services => services.AddOrkeonEmailTools(EmptyConfiguration()));

        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(ToolNames, tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Should_register_each_tool_once_When_called_twice()
    {
        var configuration = EmptyConfiguration();

        using var provider = Build(services => services.AddOrkeonEmailTools(configuration).AddOrkeonEmailTools(configuration));

        var tools = provider.GetServices<IBaseTool>().ToList();
        Assert.Equal(13, tools.Count);
        Assert.Equal(13, tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Should_bind_the_options_once_When_called_twice()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Email:Accounts:perso:Send:AllowedRecipients:0"] = "boss@example.com",
        }).Build();

        using var provider = Build(services => services.AddOrkeonEmailTools(configuration).AddOrkeonEmailTools(configuration));

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailToolsOptions>>().Value;
        Assert.Equal(["boss@example.com"], options.Accounts["perso"].Send.AllowedRecipients);
    }

    [Fact]
    public async Task Should_keep_the_tools_inert_and_say_what_to_configure()
    {
        using var provider = Build(services => services.AddOrkeonEmailTools(EmptyConfiguration()));
        var tools = provider.GetServices<IBaseTool>().ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        var accounts = await tools["email_accounts"].CallAsync(new Orkeon.Domain.Tools.Protocol.ToolCallRequest("email_accounts", []), Token);
        var folders = await tools["email_folders"].CallAsync(new Orkeon.Domain.Tools.Protocol.ToolCallRequest("email_folders", []), Token);

        Assert.True(accounts.Success);
        Assert.False(folders.Success);
        Assert.StartsWith("Tool execution failed: No e-mail account is configured.", folders.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_refuse_OAuth_tokens_until_a_host_provides_a_store()
    {
        using var provider = Build(services => services.AddOrkeonEmailTools(EmptyConfiguration()));

        var store = provider.GetRequiredService<IEmailTokenStore>();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () => await store.ReadAsync("any", Token));
        Assert.Equal(EmailErrorCode.NotConfigured, error.Code);
        Assert.NotNull(provider.GetRequiredService<EmailAccountAdministration>());
    }

    [Fact]
    public async Task Should_read_secrets_from_the_machine_process_then_user_scope_where_one_exists()
    {
        // The registered provider reads the machine's own environment. A variable no machine
        // holds is looked for in both scopes under Windows, and in the process alone elsewhere:
        // the sentence says which. Read only — no test sets a variable of the machine.
        var name = "EMAIL_TEST_" + Guid.NewGuid().ToString("N");
        var declared = TestAccounts.Gmail();
        declared.Auth.PasswordEnvVar = name;
        using var provider = Build(services => services.AddOrkeonEmailTools(EmptyConfiguration()));
        var credentials = provider.GetRequiredService<EmailCredentialProvider>();

        var error = await Assert.ThrowsAsync<EmailToolException>(async () =>
            await credentials.GetAsync(TestAccounts.Resolve("perso", declared), Token));

        var looked = OperatingSystem.IsWindows() ? "is set neither in the process environment nor in the user's" : "is not set";
        Assert.Equal(
            $"The password of e-mail account 'perso' is read from the environment variable {name}, which {looked}.",
            error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_use_the_file_system_token_store_a_host_registers_before_or_after(bool storeFirst)
    {
        var files = new FakeFileSystemService().AddMount("/credentials", FileAccessRights.ReadWrite);
        using var provider = Build(services =>
        {
            if (storeFirst)
                services.AddOrkeonEmailTokenStore(_ => files, "/credentials/email");
            services.AddOrkeonEmailTools(EmptyConfiguration());
            if (!storeFirst)
                services.AddOrkeonEmailTokenStore(_ => files, "/credentials/email");
        });

        var store = Assert.IsType<FileSystemEmailTokenStore>(provider.GetRequiredService<IEmailTokenStore>());
        await store.WriteAsync("perso", new EmailTokenSet { AccessToken = "at", ExpiresAt = DateTimeOffset.UnixEpoch }, Token);

        Assert.True(await files.ExistsAsync("/credentials/email/perso.json", Token));
    }

    [Fact]
    public void Should_dispose_the_container_synchronously_after_resolving_the_mailbox_provider()
    {
        using var provider = Build(services => services.AddOrkeonEmailTools(EmptyConfiguration()));
        var mailboxes = provider.GetRequiredService<IMailboxProvider>();
        mailboxes.GetMailbox(TestAccounts.Resolve("acct", TestAccounts.Custom()));

        var error = Record.Exception(provider.Dispose);

        Assert.Null(error);
    }

    [Fact]
    public async Task Should_dispose_the_container_asynchronously_after_resolving_the_mailbox_provider()
    {
        await using var provider = Build(services => services.AddOrkeonEmailTools(EmptyConfiguration()));
        var mailboxes = provider.GetRequiredService<IMailboxProvider>();
        mailboxes.GetMailbox(TestAccounts.Resolve("acct", TestAccounts.Custom()));

        var error = await Record.ExceptionAsync(async () => await provider.DisposeAsync());

        Assert.Null(error);
    }

    [Fact]
    public async Task Should_drop_a_live_IMAP_connection_When_the_container_is_disposed_synchronously()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        using var provider = Build(WithTestCredentials);
        await provider.GetRequiredService<IMailboxProvider>().GetMailbox(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)).ListFoldersAsync(Token);

        var error = Record.Exception(() => DisposeLikeASynchronousHost(provider));

        Assert.Null(error);
        Assert.DoesNotContain("LOGOUT", server.Commands);
    }

    [Fact]
    public async Task Should_log_a_live_IMAP_connection_out_When_the_container_is_disposed_asynchronously()
    {
        await using var server = new FakeImapServer(TestAccounts.Address, TestAccounts.Password);
        await using var provider = Build(WithTestCredentials);
        await provider.GetRequiredService<IMailboxProvider>().GetMailbox(TestAccounts.Loopback(IncomingProtocol.Imap, server.Port)).ListFoldersAsync(Token);

        await provider.DisposeAsync();

        Assert.Equal("LOGOUT", server.Commands[^1]);
    }

    [Fact]
    public void Should_refuse_missing_arguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonEmailTools(null!));
        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonEmailTokenStore(null!, "/credentials/email"));
        Assert.Throws<ArgumentException>(() => services.AddOrkeonEmailTokenStore(_ => new FakeFileSystemService(), " "));
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().AddInMemoryCollection().Build();

    /// <summary>The family, with a credential provider reading the test password instead of the process environment.</summary>
    private static void WithTestCredentials(IServiceCollection services)
    {
        services.AddSingleton(sp => new EmailCredentialProvider(
            new FakeEmailTokenStore(),
            new OAuth2Client(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), TimeProvider.System),
            TestAccounts.PasswordEnvironment()));
        services.AddOrkeonEmailTools(EmptyConfiguration());
    }

    /// <summary>What a host disposing its container synchronously does: <see cref="IDisposable.Dispose"/>, no await.</summary>
    private static void DisposeLikeASynchronousHost(IDisposable provider) => provider.Dispose();

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
