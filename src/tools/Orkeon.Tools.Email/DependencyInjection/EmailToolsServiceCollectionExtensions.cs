using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Tools;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Constants;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Security;
using Orkeon.Tools.Email.Tools;

namespace Orkeon.Tools.Email.DependencyInjection;

/// <summary>Registers the e-mail tool family.</summary>
public static class EmailToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the thirteen e-mail tools and their services, bound to the
    /// <c>Orkeon:Tools:Email</c> section of <paramref name="configuration"/>. Nothing is validated
    /// or dialled here: the tools can be listed with no account configured, and an account is
    /// checked when a call first uses it. A second call adds nothing: the first configuration
    /// wins, and the section is never bound twice.
    /// </summary>
    /// <remarks>
    /// OAuth accounts keep their tokens in the registered <see cref="IEmailTokenStore"/>. This
    /// method only registers a placeholder that refuses; the runner host replaces it with a
    /// <see cref="FileSystemEmailTokenStore"/> over its internal <c>/credentials</c> root, and a
    /// host of its own registers its store before or after this call.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration holding <c>Orkeon:Tools:Email</c>.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddOrkeonEmailTools(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EmailToolsRegistration)))
            return services;

        services.AddSingleton(new EmailToolsRegistration());
        var section = configuration.GetSection(EmailDefaults.SectionName);
        services.AddOptions<EmailToolsOptions>().Configure(options => EmailOptionsBinder.Bind(section, options));
        services.AddHttpClient(EmailDefaults.HttpClientName);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IEmailTokenStore>(_ => new UnavailableEmailTokenStore());
        services.TryAddSingleton<IEmailAccountRegistry>(sp => new EmailAccountRegistry(sp.GetRequiredService<IOptions<EmailToolsOptions>>()));
        services.TryAddSingleton(sp => new OAuth2Client(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(EmailDefaults.HttpClientName),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new EmailCredentialProvider(
            sp.GetRequiredService<IEmailTokenStore>(),
            sp.GetRequiredService<OAuth2Client>(),
            EmailEnvironment.Machine));
        services.TryAddSingleton<IMailServiceConnector>(_ => new NetworkMailServiceConnector());
        services.TryAddSingleton<IMailboxProvider>(sp => new MailboxProvider(
            sp.GetRequiredService<IMailServiceConnector>(),
            sp.GetRequiredService<EmailCredentialProvider>(),
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(EmailDefaults.HttpClientName),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new EmailAccess(sp.GetRequiredService<IEmailAccountRegistry>(), sp.GetRequiredService<IMailboxProvider>()));
        services.TryAddSingleton(sp => new EmailContentScreen(sp.GetRequiredService<IOptions<EmailToolsOptions>>()));
        services.TryAddSingleton(sp => new SendQuota(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new EmailAccountAdministration(
            sp.GetRequiredService<IEmailAccountRegistry>(),
            sp.GetRequiredService<EmailCredentialProvider>(),
            sp.GetRequiredService<OAuth2Client>(),
            sp.GetRequiredService<IMailboxProvider>()));

        AddTool(services, sp => new EmailAccountsTool(sp.GetRequiredService<EmailAccountAdministration>(), Logger<EmailAccountsTool>(sp)));
        AddTool(services, sp => new EmailFoldersTool(sp.GetRequiredService<EmailAccess>(), Logger<EmailFoldersTool>(sp)));
        AddTool(services, sp => new EmailSearchTool(sp.GetRequiredService<EmailAccess>(), sp.GetRequiredService<EmailContentScreen>(), Logger<EmailSearchTool>(sp)));
        AddTool(services, sp => new EmailReadTool(sp.GetRequiredService<EmailAccess>(), sp.GetRequiredService<EmailContentScreen>(), Logger<EmailReadTool>(sp)));
        AddTool(services, sp => new EmailSaveAttachmentTool(sp.GetRequiredService<EmailAccess>(), sp.GetRequiredService<IFileSystemService>(), Logger<EmailSaveAttachmentTool>(sp)));
        AddTool(services, sp => new EmailCreateFolderTool(sp.GetRequiredService<EmailAccess>(), Logger<EmailCreateFolderTool>(sp)));
        AddTool(services, sp => new EmailRenameFolderTool(sp.GetRequiredService<EmailAccess>(), Logger<EmailRenameFolderTool>(sp)));
        AddTool(services, sp => new EmailMoveTool(sp.GetRequiredService<EmailAccess>(), Logger<EmailMoveTool>(sp)));
        AddTool(services, sp => new EmailMarkTool(sp.GetRequiredService<EmailAccess>(), Logger<EmailMarkTool>(sp)));
        AddTool(services, sp => new EmailDeleteTool(sp.GetRequiredService<EmailAccess>(), Logger<EmailDeleteTool>(sp)));
        AddTool(services, sp => new EmailDraftTool(sp.GetRequiredService<EmailAccess>(), sp.GetRequiredService<IFileSystemService>(), Logger<EmailDraftTool>(sp)));
        AddTool(services, sp => new EmailSendTool(
            sp.GetRequiredService<EmailAccess>(), sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<SendQuota>(),
            sp.GetRequiredService<TimeProvider>(), Logger<EmailSendTool>(sp)));
        AddTool(services, sp => new EmailParserTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<EmailContentScreen>(), Logger<EmailParserTool>(sp)));
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
    public static IServiceCollection AddOrkeonEmailTokenStore(
        this IServiceCollection services, Func<IServiceProvider, IFileSystemService> fileSystem, string virtualDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDirectory);
        services.Replace(ServiceDescriptor.Singleton<IEmailTokenStore>(sp => new FileSystemEmailTokenStore(fileSystem(sp), virtualDirectory)));
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
    /// list setting (the recipient allow-list among them) to itself.
    /// </summary>
    private sealed class EmailToolsRegistration;
}
