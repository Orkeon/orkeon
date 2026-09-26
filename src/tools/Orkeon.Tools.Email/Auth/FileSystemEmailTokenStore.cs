using System.Text.Json;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Email.Auth;

/// <summary>
/// Keeps each account's tokens in one JSON file under a virtual directory. The host hands it
/// the privileged view of the VFS over an internal root (<c>/credentials</c>), which no
/// agent-facing tool resolves (ADR-008). The file is plain JSON: it is shielded from the VFS
/// tools, not from a shell or code tool running as the same operating-system user.
/// </summary>
public sealed class FileSystemEmailTokenStore : IEmailTokenStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly IFileSystemService _fileSystem;
    private readonly string _directory;

    /// <summary>Creates the store over <paramref name="virtualDirectory"/> of <paramref name="fileSystem"/>.</summary>
    /// <param name="fileSystem">The file system the tokens are written through.</param>
    /// <param name="virtualDirectory">The virtual directory holding one file per account.</param>
    public FileSystemEmailTokenStore(IFileSystemService fileSystem, string virtualDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDirectory);
        _fileSystem = fileSystem;
        _directory = virtualDirectory.TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<EmailTokenSet?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        var json = await _fileSystem.TryReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<EmailTokenSet>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // A damaged token file is the same as no token: the account asks for a new sign-in.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string key, EmailTokenSet tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var path = PathOf(key);
        var json = JsonSerializer.Serialize(tokens, SerializerOptions);

        await _fileSystem.CreateDirectoryAsync(_directory, cancellationToken).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        if (!await _fileSystem.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
            return false;
        return await _fileSystem.DeleteAsync(path, recursive: false, cancellationToken).ConfigureAwait(false);
    }

    private string PathOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') || key.StartsWith('.'))
            throw new ArgumentException("A token key holds letters, digits, '.', '_' and '-' only, and does not start with '.'.", nameof(key));
        return $"{_directory}/{key}.json";
    }
}
