using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// Stores secrets encrypted by Windows DPAPI (Data Protection API) via the virtual file system.
/// Only available on Windows. The secrets directory is created lazily (and exactly once) on the
/// first read/write/list operation, so no blocking initialization is required at DI composition time.
/// Implements <see cref="IWritableSecretProvider"/>: key provisioning flows
/// (<c>IKeyRotationService.ProvisionNewKeyAsync</c>) can persist new encryption keys through it.
/// </summary>
public sealed partial class DpapiSecretProvider : IWritableSecretProvider, IDisposable
{
    private readonly IFileSystemService _fs;
    private readonly string _vDir;
    private readonly ILogger<DpapiSecretProvider> _logger;

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private volatile bool _initialized;

    /// <summary>Initializes a new instance of <see cref="DpapiSecretProvider"/>.</summary>
    public DpapiSecretProvider(
        IFileSystemService fs,
        string secretsVirtualDir,
        ILogger<DpapiSecretProvider> logger)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only available on Windows");

        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsVirtualDir);
        ArgumentNullException.ThrowIfNull(logger);

        _fs = fs;
        _vDir = secretsVirtualDir;
        _logger = logger;
    }

    /// <summary>
    /// Creates the secrets directory. Invoked lazily (once) by the first read/write/list operation.
    /// Safe to call explicitly and repeatedly; the directory is created at most once.
    /// </summary>
    public async System.Threading.Tasks.Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await _fs.CreateDirectoryAsync(_vDir, ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <inheritdoc />
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public System.Threading.Tasks.Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        return GetSecretCoreAsync(secretName, ct);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async System.Threading.Tasks.Task<SecretValue> GetSecretCoreAsync(string secretName, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var vPath = VPath(secretName);
        if (!await _fs.ExistsAsync(vPath, ct).ConfigureAwait(false))
            throw new KeyNotFoundException($"Secret '{secretName}' not found in DPAPI store");

        var encrypted = await _fs.TryReadAllBytesAsync(vPath, ct).ConfigureAwait(false);
        if (encrypted is null)
            throw new KeyNotFoundException($"Secret '{secretName}' not found in DPAPI store");

        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return new SecretValue(Encoding.UTF8.GetString(decrypted), "dpapi");
    }

    /// <inheritdoc />
    /// <remarks>Stores a secret encrypted by DPAPI (initial setup and key provisioning).</remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public System.Threading.Tasks.Task StoreSecretAsync(string secretName, string value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentNullException.ThrowIfNull(value);
        return StoreSecretCoreAsync(secretName, value, ct);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async System.Threading.Tasks.Task StoreSecretCoreAsync(string secretName, string value, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        await _fs.WriteAllBytesAsync(VPath(secretName), encrypted, ct).ConfigureAwait(false);
        LogSecretStoredInDpapiStore(secretName);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
        => await _fs.ExistsAsync(VPath(secretName), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        if (!await _fs.ExistsAsync(_vDir, ct).ConfigureAwait(false))
            return Array.Empty<string>();

        var opts = new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*.dpapi");
        var names = new List<string>();

        await foreach (var entry in _fs.EnumerateFilesAsync(_vDir, opts, ct).ConfigureAwait(false))
        {
            if (entry.Kind != VirtualEntryKind.File) continue;
            names.Add(Path.GetFileNameWithoutExtension(entry.VirtualPath));
        }

        return names;
    }

    /// <summary>Disposes the lazy-initialization gate.</summary>
    public void Dispose() => _initGate.Dispose();

    private string VPath(string secretName) => $"{_vDir}/{secretName}.dpapi";

    [LoggerMessage(Level = LogLevel.Debug, Message = "Secret {Name} stored in DPAPI store")]
    private partial void LogSecretStoredInDpapiStore(object name);
}
