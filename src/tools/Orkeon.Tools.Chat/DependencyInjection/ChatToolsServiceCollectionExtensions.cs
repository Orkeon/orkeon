using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Administration;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tools;

namespace Orkeon.Tools.Chat.DependencyInjection;

/// <summary>Registers the chat tool family (Microsoft Teams).</summary>
public static class ChatToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ten chat tools and their services, bound to the
    /// <c>Orkeon:Tools:Chat</c> section of <paramref name="configuration"/>. Nothing is validated
    /// or dialled here: the tools can be listed with no account configured, and an account is
    /// checked when a call first uses it. A second call adds nothing: the first configuration
    /// wins, and the section is never bound twice.
    /// </summary>
    /// <remarks>
    /// Accounts keep their OAuth tokens in the registered <see cref="IChatTokenStore"/>. This
    /// method only registers a placeholder that refuses; the runner host replaces it with a
    /// <see cref="FileSystemChatTokenStore"/> over its internal <c>/credentials</c> root, and a
    /// host of its own registers its store before or after this call.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration holding <c>Orkeon:Tools:Chat</c>.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddOrkeonChatTools(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ChatToolsRegistration)))
            return services;

        services.AddSingleton(new ChatToolsRegistration());
        var section = configuration.GetSection(ChatDefaults.SectionName);
        services.AddOptions<ChatToolsOptions>().Configure(options => ChatOptionsBinder.Bind(section, options));
        // No redirect is followed: a token or a message never goes to a host that was not chosen here.
        services.AddHttpClient(ChatDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        // The address of a webhook is its secret: the client logs no request either.
        services.AddHttpClient(ChatDefaults.WebhookHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        // Non-LLM clients: the LLM exchange log does not capture their calls nor what they carry.
        services.AddSingleton(new LlmExchangeLogExclusion(ChatDefaults.HttpClientName));
        services.AddSingleton(new LlmExchangeLogExclusion(ChatDefaults.WebhookHttpClientName));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IChatTokenStore>(_ => new UnavailableChatTokenStore());
        services.TryAddSingleton<IChatAccountRegistry>(sp => new ChatAccountRegistry(sp.GetRequiredService<IOptions<ChatToolsOptions>>()));
        // The family's own OAuth client, over its own named HTTP client: the e-mail family keeps its own.
        services.TryAddSingleton(sp => new ChatOAuth(new OAuth2Client(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(ChatDefaults.HttpClientName),
            sp.GetRequiredService<TimeProvider>())));
        services.TryAddSingleton(sp => new ChatCredentialProvider(
            sp.GetRequiredService<IChatTokenStore>(), sp.GetRequiredService<ChatOAuth>().Client));
        services.TryAddSingleton(sp => ChatSessions.Over(
            sp.GetRequiredService<ChatCredentialProvider>(),
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(ChatDefaults.HttpClientName),
            sp.GetRequiredService<TimeProvider>(),
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(ChatDefaults.WebhookHttpClientName)));
        services.TryAddSingleton(sp => new ChatAccess(sp.GetRequiredService<IChatAccountRegistry>(), sp.GetRequiredService<ChatSessions>()));
        services.TryAddSingleton(sp => new ChatContentScreen(sp.GetRequiredService<IOptions<ChatToolsOptions>>()));
        services.TryAddSingleton(sp => new SendQuota(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new PostPacer(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new WriteTurns(sp.GetRequiredService<SendQuota>(), sp.GetRequiredService<PostPacer>()));
        services.TryAddSingleton(sp => new ChatAccountAdministration(
            sp.GetRequiredService<IChatAccountRegistry>(),
            sp.GetRequiredService<ChatCredentialProvider>(),
            sp.GetRequiredService<ChatOAuth>().Client,
            sp.GetRequiredService<ChatSessions>()));
        services.TryAddSingleton(sp => new ChatRightsCheck(sp.GetRequiredService<ChatAccountAdministration>()));

        AddTool(services, sp => new ChatAccountsTool(sp.GetRequiredService<ChatAccountAdministration>(), Logger<ChatAccountsTool>(sp)));
        AddTool(services, sp => new ChatListTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<ChatContentScreen>(), Logger<ChatListTool>(sp)));
        AddTool(services, sp => new ChatReadTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<ChatContentScreen>(), Logger<ChatReadTool>(sp)));
        AddTool(services, sp => new ChatMembersTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<ChatContentScreen>(), Logger<ChatMembersTool>(sp)));
        AddTool(services, sp => new ChatSearchTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<ChatContentScreen>(), Logger<ChatSearchTool>(sp)));
        AddTool(services, sp => new ChatMeetingsTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<ChatContentScreen>(), Logger<ChatMeetingsTool>(sp)));
        AddTool(services, sp => new ChatSendTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<WriteTurns>(), Logger<ChatSendTool>(sp)));
        AddTool(services, sp => new ChatEditTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<WriteTurns>(), Logger<ChatEditTool>(sp)));
        AddTool(services, sp => new ChatDeleteTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<WriteTurns>(), Logger<ChatDeleteTool>(sp)));
        AddTool(services, sp => new ChatReactTool(sp.GetRequiredService<ChatAccess>(), sp.GetRequiredService<WriteTurns>(), Logger<ChatReactTool>(sp)));
        return services;
    }

    /// <summary>
    /// Replaces the token store with one over <paramref name="virtualDirectory"/> of the file
    /// system <paramref name="fileSystem"/> returns — for the runner host, its privileged view of
    /// the internal <c>/credentials</c> root.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="fileSystem">Returns the file system the tokens go through.</param>
    /// <param name="virtualDirectory">The virtual directory holding one token file per account.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddOrkeonChatTokenStore(
        this IServiceCollection services, Func<IServiceProvider, IFileSystemService> fileSystem, string virtualDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDirectory);
        services.Replace(ServiceDescriptor.Singleton<IChatTokenStore>(sp => new FileSystemChatTokenStore(fileSystem(sp), virtualDirectory)));
        return services;
    }

    // Transient like the other families' tools; TryAddEnumerable keeps a second registration
    // call from adding a duplicate name, which the name-keyed registry refuses.
    private static void AddTool<TTool>(IServiceCollection services, Func<IServiceProvider, TTool> factory)
        where TTool : class, IBaseTool =>
        services.TryAddEnumerable(ServiceDescriptor.Transient<IBaseTool, TTool>(factory));

    private static ILogger<T>? Logger<T>(IServiceProvider services) => services.GetService<ILogger<T>>();

    /// <summary>
    /// Marks the family as registered. Binding the section a second time would append every
    /// list setting (the allowed places among them) to itself.
    /// </summary>
    private sealed class ChatToolsRegistration;
}

/// <summary>
/// The chat family's OAuth client, under a type of its own: the e-mail family registers an
/// <see cref="OAuth2Client"/> too, and each must keep the HTTP client it was built with.
/// </summary>
/// <param name="Client">The client.</param>
internal sealed record ChatOAuth(OAuth2Client Client);
