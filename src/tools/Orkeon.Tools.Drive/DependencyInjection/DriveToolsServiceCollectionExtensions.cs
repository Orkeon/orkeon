using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Administration;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Security;
using Orkeon.Tools.Drive.Tools;

namespace Orkeon.Tools.Drive.DependencyInjection;

/// <summary>Registers the drive tool family (Google Drive, OneDrive).</summary>
public static class DriveToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the eleven drive tools and their services, bound to the
    /// <c>Orkeon:Tools:Drive</c> section of <paramref name="configuration"/>. Nothing is validated
    /// or dialled here: the tools can be listed with no account configured, and an account is
    /// checked when a call first uses it. A second call adds nothing: the first configuration
    /// wins, and the section is never bound twice.
    /// </summary>
    /// <remarks>
    /// Accounts keep their OAuth tokens in the registered <see cref="IDriveTokenStore"/>. This
    /// method only registers a placeholder that refuses; the runner host replaces it with a
    /// <see cref="FileSystemDriveTokenStore"/> over its internal <c>/credentials</c> root, and a
    /// host of its own registers its store before or after this call.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration holding <c>Orkeon:Tools:Drive</c>.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddOrkeonDriveTools(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(DriveToolsRegistration)))
            return services;

        services.AddSingleton(new DriveToolsRegistration());
        var section = configuration.GetSection(DriveDefaults.SectionName);
        services.AddOptions<DriveToolsOptions>().Configure(options => DriveOptionsBinder.Bind(section, options));
        services.AddHttpClient(DriveDefaults.HttpClientName);
        // A non-LLM client: the LLM exchange log does not capture its calls nor what they carry.
        services.AddSingleton(new LlmExchangeLogExclusion(DriveDefaults.HttpClientName));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IDriveTokenStore>(_ => new UnavailableDriveTokenStore());
        services.TryAddSingleton<IDriveAccountRegistry>(sp => new DriveAccountRegistry(sp.GetRequiredService<IOptions<DriveToolsOptions>>()));
        // The family's own OAuth client, over its own named HTTP client: the e-mail family keeps its own.
        services.TryAddSingleton(sp => new DriveOAuth(new OAuth2Client(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(DriveDefaults.HttpClientName),
            sp.GetRequiredService<TimeProvider>())));
        services.TryAddSingleton(sp => new DriveCredentialProvider(
            sp.GetRequiredService<IDriveTokenStore>(),
            sp.GetRequiredService<DriveOAuth>().Client,
            SecretEnvironment.Machine));
        services.TryAddSingleton<IDriveFactory>(sp => new DriveFactory(
            sp.GetRequiredService<DriveCredentialProvider>(),
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(DriveDefaults.HttpClientName)));
        services.TryAddSingleton(sp => new DriveAccess(sp.GetRequiredService<IDriveAccountRegistry>(), sp.GetRequiredService<IDriveFactory>()));
        services.TryAddSingleton(sp => new DriveContentScreen(sp.GetRequiredService<IOptions<DriveToolsOptions>>()));
        services.TryAddSingleton(sp => new DriveAccountAdministration(
            sp.GetRequiredService<IDriveAccountRegistry>(),
            sp.GetRequiredService<DriveCredentialProvider>(),
            sp.GetRequiredService<DriveOAuth>().Client,
            sp.GetRequiredService<IDriveFactory>()));
        services.TryAddSingleton(sp => new DriveRightsCheck(sp.GetRequiredService<DriveAccountAdministration>()));

        AddTool(services, sp => new DriveAccountsTool(sp.GetRequiredService<DriveAccountAdministration>(), Logger<DriveAccountsTool>(sp)));
        AddTool(services, sp => new DriveListTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveListTool>(sp)));
        AddTool(services, sp => new DriveSearchTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveSearchTool>(sp)));
        AddTool(services, sp => new DriveInfoTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveInfoTool>(sp)));
        AddTool(services, sp => new DriveReadTool(sp.GetRequiredService<DriveAccess>(), sp.GetRequiredService<DriveContentScreen>(), Logger<DriveReadTool>(sp)));
        AddTool(services, sp => new DriveDownloadTool(sp.GetRequiredService<DriveAccess>(), sp.GetRequiredService<IFileSystemService>(), Logger<DriveDownloadTool>(sp)));
        AddTool(services, sp => new DriveUploadTool(sp.GetRequiredService<DriveAccess>(), sp.GetRequiredService<IFileSystemService>(), Logger<DriveUploadTool>(sp)));
        AddTool(services, sp => new DriveCreateFolderTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveCreateFolderTool>(sp)));
        AddTool(services, sp => new DriveMoveTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveMoveTool>(sp)));
        AddTool(services, sp => new DriveDeleteTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveDeleteTool>(sp)));
        AddTool(services, sp => new DriveShareTool(sp.GetRequiredService<DriveAccess>(), Logger<DriveShareTool>(sp)));
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
    public static IServiceCollection AddOrkeonDriveTokenStore(
        this IServiceCollection services, Func<IServiceProvider, IFileSystemService> fileSystem, string virtualDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDirectory);
        services.Replace(ServiceDescriptor.Singleton<IDriveTokenStore>(sp => new FileSystemDriveTokenStore(fileSystem(sp), virtualDirectory)));
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
    /// list setting (the share allow-list among them) to itself.
    /// </summary>
    private sealed class DriveToolsRegistration;
}

/// <summary>
/// The drive family's OAuth client, under a type of its own: the e-mail family registers an
/// <see cref="OAuth2Client"/> too, and each must keep the HTTP client it was built with.
/// </summary>
/// <param name="Client">The client.</param>
internal sealed record DriveOAuth(OAuth2Client Client);
