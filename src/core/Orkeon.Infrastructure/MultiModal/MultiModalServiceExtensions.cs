using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.MultiModal;
using Orkeon.Application.Validation;

namespace Orkeon.Infrastructure.MultiModal;

/// <summary>
/// Extension methods for registering multi-modal content services in the DI container.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-003): multi-modal content support is NOT registered
/// by <c>AddOrkeonInfrastructure()</c>. Hosts working with multi-modal content call
/// <c>AddOrkeonMultiModal(...)</c> explicitly. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// <para>
/// Since R3.9 the vision pipeline is real: <see cref="IMultiModalContentLoader"/> loads
/// image files from the virtual file system, and messages carrying
/// <c>LlmMessage.MultiModalContent</c> are sent as structured vision payloads by the
/// Anthropic (image content blocks) and OpenAI (<c>image_url</c> parts) providers.
/// Resolving <see cref="IMultiModalContentLoader"/> requires an
/// <c>IFileSystemService</c> registration (e.g. <c>AddOrkeonFileSystem(configuration)</c>).
/// </para>
/// </summary>
public static class MultiModalServiceExtensions
{
    /// <summary>
    /// Adds Orkeon multi-modal content support services: content validation
    /// (<see cref="IContentValidationService"/>) and image loading from the virtual file
    /// system (<see cref="IMultiModalContentLoader"/>).
    /// When a configuration is provided, binds <see cref="MultiModalOptions"/> from the
    /// "Orkeon:MultiModal" section; otherwise registers default options.
    /// </summary>
    public static IServiceCollection AddOrkeonMultiModal(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<MultiModalOptions>(
                configuration.GetSection("Orkeon:MultiModal"));
        }
        else
        {
            services.TryAddSingleton(Options.Create(new MultiModalOptions()));
        }

        services.TryAddSingleton<IContentValidationService, ContentValidationService>();

        // Requires IFileSystemService (AddOrkeonFileSystem) to be resolvable.
        services.TryAddSingleton<IMultiModalContentLoader, MultiModalContentLoader>();
        return services;
    }
}
