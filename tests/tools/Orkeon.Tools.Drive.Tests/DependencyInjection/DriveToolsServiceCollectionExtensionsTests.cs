using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Administration;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.DependencyInjection;

namespace Orkeon.Tools.Drive.Tests.DependencyInjection;

/// <summary>What <c>AddOrkeonDriveTools</c> registers, and how a host gives it a token store.</summary>
public sealed class DriveToolsServiceCollectionExtensionsTests
{
    private static readonly string[] ToolNames =
    [
        "drive_accounts", "drive_create_folder", "drive_delete", "drive_download", "drive_info", "drive_list",
        "drive_move", "drive_read", "drive_search", "drive_share", "drive_upload",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Should_register_the_eleven_tools_with_no_drive_configuration_at_all()
    {
        using var provider = Build(services => services.AddOrkeonDriveTools(EmptyConfiguration()));

        Assert.Equal(ToolNames, provider.GetServices<IBaseTool>().Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.NotNull(provider.GetRequiredService<DriveRightsCheck>());
        Assert.Contains(provider.GetServices<LlmExchangeLogExclusion>(), exclusion => exclusion.ClientName == "orkeon.drive");
    }

    [Fact]
    public void Should_register_each_tool_once_and_bind_once_When_called_twice()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Drive:Accounts:perso:Share:AllowedRecipients:0"] = "boss@example.com",
        }).Build();

        using var provider = Build(services => services.AddOrkeonDriveTools(configuration).AddOrkeonDriveTools(configuration));

        Assert.Equal(11, provider.GetServices<IBaseTool>().Count());
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DriveToolsOptions>>().Value;
        Assert.Equal(["boss@example.com"], options.Accounts["perso"].Share.AllowedRecipients);
    }

    [Fact]
    public void Should_live_beside_the_e_mail_family_with_an_OAuth_client_of_its_own()
    {
        using var http = new HttpClient();
        using var provider = Build(services =>
        {
            services.AddSingleton(new OAuth2Client(http, TimeProvider.System));
            services.AddOrkeonDriveTools(EmptyConfiguration());
        });

        Assert.NotSame(provider.GetRequiredService<OAuth2Client>(), provider.GetRequiredService<DriveOAuth>().Client);
    }

    [Fact]
    public async Task Should_keep_the_tools_inert_and_say_what_to_configure()
    {
        using var provider = Build(services => services.AddOrkeonDriveTools(EmptyConfiguration()));
        var tools = provider.GetServices<IBaseTool>().ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        var accounts = await tools["drive_accounts"].CallAsync(new Orkeon.Domain.Tools.Protocol.ToolCallRequest("drive_accounts", []), Token);
        var list = await tools["drive_list"].CallAsync(new Orkeon.Domain.Tools.Protocol.ToolCallRequest("drive_list", []), Token);

        Assert.True(accounts.Success);
        Assert.False(list.Success);
        Assert.StartsWith("Tool execution failed: No drive account is configured.", list.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_refuse_tokens_until_a_host_provides_a_store()
    {
        using var provider = Build(services => services.AddOrkeonDriveTools(EmptyConfiguration()));

        var error = await Assert.ThrowsAsync<DriveToolException>(async () => await provider.GetRequiredService<IDriveTokenStore>().ReadAsync("any", Token));

        Assert.Equal(DriveErrorCode.NotConfigured, error.Code);
        Assert.NotNull(provider.GetRequiredService<DriveAccountAdministration>());
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
                services.AddOrkeonDriveTokenStore(_ => files, "/credentials/drive");
            services.AddOrkeonDriveTools(EmptyConfiguration());
            if (!storeFirst)
                services.AddOrkeonDriveTokenStore(_ => files, "/credentials/drive");
        });

        var store = Assert.IsType<FileSystemDriveTokenStore>(provider.GetRequiredService<IDriveTokenStore>());
        await store.WriteAsync("perso", new DriveTokenSet { AccessToken = "at", ExpiresAt = DateTimeOffset.UnixEpoch }, Token);

        Assert.True(await files.ExistsAsync("/credentials/drive/perso.json", Token));
        Assert.Equal("at", (await store.ReadAsync("perso", Token))!.AccessToken);
        Assert.True(await store.DeleteAsync("perso", Token));
        Assert.False(await store.DeleteAsync("perso", Token));
    }

    [Fact]
    public void Should_refuse_missing_arguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonDriveTools(null!));
        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonDriveTokenStore(null!, "/credentials/drive"));
        Assert.Throws<ArgumentException>(() => services.AddOrkeonDriveTokenStore(_ => new FakeFileSystemService(), " "));
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
