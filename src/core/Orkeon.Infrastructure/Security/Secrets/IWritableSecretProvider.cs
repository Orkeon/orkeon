using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// A secret provider that can also persist secrets. Used by key provisioning and
/// rotation flows (R2.8) to durably store a newly generated encryption key.
/// </summary>
public interface IWritableSecretProvider : ISecretProvider
{
    /// <summary>
    /// Stores (creates or replaces) a secret value under the given logical name.
    /// </summary>
    /// <param name="secretName">The logical name of the secret.</param>
    /// <param name="value">The secret value to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task StoreSecretAsync(string secretName, string value, CancellationToken ct = default);
}
