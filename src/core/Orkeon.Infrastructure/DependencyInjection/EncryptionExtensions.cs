using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Encryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering memory encryption services.
/// </summary>
public static class EncryptionExtensions
{
    /// <summary>
    /// Adds the AES-256-GCM encryption provider used by encrypted memory decorators.
    /// Configuration is read from the "Orkeon:Encryption" section.
    /// </summary>
    /// <remarks>
    /// Key rotation is a dormant subsystem and is NOT registered here; enable it
    /// explicitly with <see cref="AddOrkeonKeyRotation"/>
    /// (R4.9 — see <c>docs/reference/opt-in-subsystems.md</c>).
    /// </remarks>
    public static IServiceCollection AddOrkeonEncryption(this IServiceCollection services)
    {
        services.AddOptions<AesEncryptionOptions>()
            .BindConfiguration("Orkeon:Encryption");

        services.TryAddSingleton<IEncryptionProvider, AesEncryptionProvider>();

        return services;
    }

    /// <summary>
    /// Opt-in: adds the key rotation service (<see cref="IKeyRotationService"/>).
    /// Not registered by <c>AddOrkeonInfrastructure()</c> — call this explicitly when
    /// the host needs to re-encrypt stored memory under a new key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rotation is real (R2.8): <see cref="IKeyRotationService.RotateAsync"/> walks the raw
    /// memory store in two phases (staging, then switch), decrypting each entry with the old
    /// provider and re-encrypting it with the new one — with rollback on staging failure,
    /// idempotent resume (roll-forward) on switch failure, and persisted key-version metadata.
    /// <see cref="IKeyRotationService.ProvisionNewKeyAsync"/> generates and persists the new key
    /// through an <see cref="Orkeon.Infrastructure.Security.Secrets.IWritableSecretProvider"/>.
    /// </para>
    /// <para>
    /// The store and the old/new <see cref="IEncryptionProvider"/> instances are passed as method
    /// arguments, so this extension is self-contained: it only requires logging to be configured
    /// by the host. Pair it with <see cref="AddOrkeonEncryption"/> to obtain providers from DI.
    /// See <c>docs/reference/opt-in-subsystems.md</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrkeonKeyRotation(this IServiceCollection services)
    {
        services.TryAddSingleton<IKeyRotationService, KeyRotationService>();
        return services;
    }
}
