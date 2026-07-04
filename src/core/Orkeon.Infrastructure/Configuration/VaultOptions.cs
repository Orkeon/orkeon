using Orkeon.Infrastructure.Constants.Orchestration;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>Configuration options for secret vault providers.</summary>
public class VaultOptions
{
    /// <summary>URI of Azure Key Vault (e.g. https://my-vault.vault.azure.net/)</summary>
    public Uri? AzureKeyVaultUri { get; set; }

    /// <summary>Whether to use AWS Secrets Manager</summary>
    public bool UseAwsSecretsManager { get; set; }

    /// <summary>Directory for DPAPI secret store (Windows only)</summary>
    public string? DpapiSecretsDirectory { get; set; }

    /// <summary>Cache TTL for secrets in memory (default: 5 min)</summary>
    public TimeSpan CacheTtl { get; set; } = OrchestrationDefaults.SecretCacheTtl;
}
