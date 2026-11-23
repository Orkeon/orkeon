using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Administration;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.DependencyInjection;

namespace Orkeon.Tools.Chat.Tests.DependencyInjection;

/// <summary>What <c>AddOrkeonChatTools</c> registers, and how a host gives it a token store.</summary>
public sealed class ChatToolsServiceCollectionExtensionsTests
{
    private static readonly string[] ToolNames =
    ["chat_accounts", "chat_delete", "chat_edit", "chat_list", "chat_meetings", "chat_members", "chat_react", "chat_read", "chat_search", "chat_send"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Should_register_the_tools_with_no_chat_configuration_at_all()
    {
        using var provider = Build(services => services.AddOrkeonChatTools(EmptyConfiguration()));

        Assert.Equal(ToolNames, provider.GetServices<IBaseTool>().Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.NotNull(provider.GetRequiredService<ChatRightsCheck>());
        Assert.Contains(provider.GetServices<LlmExchangeLogExclusion>(), exclusion => exclusion.ClientName == "orkeon.chat");
        Assert.Contains(provider.GetServices<LlmExchangeLogExclusion>(), exclusion => exclusion.ClientName == "orkeon.chat.webhook");
    }

    [Fact]
    public void Should_register_each_tool_once_and_bind_once_When_called_twice()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orkeon:Tools:Chat:Accounts:work:Send:AllowedPlaces:0"] = "user:ada@example.org",
        }).Build();

        using var provider = Build(services => services.AddOrkeonChatTools(configuration).AddOrkeonChatTools(configuration));

        Assert.Equal(ToolNames.Length, provider.GetServices<IBaseTool>().Count());
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChatToolsOptions>>().Value;
        Assert.Equal(["user:ada@example.org"], options.Accounts["work"].Send.AllowedPlaces);
    }

    [Fact]
    public void Should_live_beside_the_e_mail_family_with_an_OAuth_client_of_its_own()
    {
        using var http = new HttpClient();
        using var provider = Build(services =>
        {
            services.AddSingleton(new OAuth2Client(http, TimeProvider.System));
            services.AddOrkeonChatTools(EmptyConfiguration());
        });

        Assert.NotSame(provider.GetRequiredService<OAuth2Client>(), provider.GetRequiredService<ChatOAuth>().Client);
    }

    [Fact]
    public void Should_follow_no_redirect_of_a_webhook()
    {
        using var provider = Build(services => services.AddOrkeonChatTools(EmptyConfiguration()));

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("orkeon.chat.webhook");
        var chain = new List<HttpMessageHandler>();
        for (; handler is not null; handler = (handler as DelegatingHandler)?.InnerHandler)
            chain.Add(handler);

        Assert.False(Assert.IsType<SocketsHttpHandler>(chain[^1]).AllowAutoRedirect);
        Assert.DoesNotContain(chain, link => link.GetType().Name.Contains("Logging", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_keep_the_tools_inert_and_say_what_to_configure()
    {
        using var provider = Build(services => services.AddOrkeonChatTools(EmptyConfiguration()));
        var tools = provider.GetServices<IBaseTool>().ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        var accounts = await tools["chat_accounts"].CallAsync(new Orkeon.Domain.Tools.Protocol.ToolCallRequest("chat_accounts", []), Token);
        var list = await tools["chat_list"].CallAsync(new Orkeon.Domain.Tools.Protocol.ToolCallRequest("chat_list", []), Token);

        Assert.True(accounts.Success);
        Assert.False(list.Success);
        Assert.StartsWith("Tool execution failed: No chat account is configured.", list.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_refuse_tokens_until_a_host_provides_a_store()
    {
        using var provider = Build(services => services.AddOrkeonChatTools(EmptyConfiguration()));

        var error = await Assert.ThrowsAsync<ChatToolException>(async () => await provider.GetRequiredService<IChatTokenStore>().ReadAsync("any", Token));

        Assert.Equal(ChatErrorCode.NotConfigured, error.Code);
        Assert.NotNull(provider.GetRequiredService<ChatAccountAdministration>());
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
                services.AddOrkeonChatTokenStore(_ => files, "/credentials/chat");
            services.AddOrkeonChatTools(EmptyConfiguration());
            if (!storeFirst)
                services.AddOrkeonChatTokenStore(_ => files, "/credentials/chat");
        });

        var store = Assert.IsType<FileSystemChatTokenStore>(provider.GetRequiredService<IChatTokenStore>());
        await store.WriteAsync("perso", new ChatTokenSet { AccessToken = "at", ExpiresAt = DateTimeOffset.UnixEpoch }, Token);

        Assert.True(await files.ExistsAsync("/credentials/chat/perso.json", Token));
        Assert.Equal("at", (await store.ReadAsync("perso", Token))!.AccessToken);
        Assert.True(await store.DeleteAsync("perso", Token));
        Assert.False(await store.DeleteAsync("perso", Token));
    }

    [Fact]
    public void Should_refuse_missing_arguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonChatTools(null!));
        Assert.Throws<ArgumentNullException>(() => services.AddOrkeonChatTokenStore(null!, "/credentials/chat"));
        Assert.Throws<ArgumentException>(() => services.AddOrkeonChatTokenStore(_ => new FakeFileSystemService(), " "));
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
