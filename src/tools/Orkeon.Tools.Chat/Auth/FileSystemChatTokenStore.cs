using System.Text.Json;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Chat.Auth;

/// <summary>
/// Keeps each account's tokens in one JSON file under a virtual directory. The host hands it
/// the privileged view of the VFS over an internal root (<c>/credentials</c>), which no
/// agent-facing tool resolves (ADR-008). The file is plain JSON: it is shielded from the VFS
/// tools, not from a shell or code tool running as the same operating-system user.
/// </summary>
public sealed class FileSystemChatTokenStore : IChatTokenStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private const string Suffix = ".json";
    private const string PartialSuffix = Suffix + ".tmp";
    private const int DigestLength = 12;

    private static readonly System.Buffers.SearchValues<char> Digits = System.Buffers.SearchValues.Create("0123456789abcdef");

    private readonly IFileSystemService _fileSystem;
    private readonly string _directory;

    /// <summary>Creates the store over <paramref name="virtualDirectory"/> of <paramref name="fileSystem"/>.</summary>
    /// <param name="fileSystem">The file system the tokens are written through.</param>
    /// <param name="virtualDirectory">The virtual directory holding one file per account.</param>
    public FileSystemChatTokenStore(IFileSystemService fileSystem, string virtualDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualDirectory);
        _fileSystem = fileSystem;
        _directory = virtualDirectory.TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<ChatTokenSet?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        var json = await _fileSystem.TryReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ChatTokenSet>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // A damaged token file is the same as no token: the account asks for a new sign-in.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string key, ChatTokenSet tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var path = PathOf(key);
        var json = JsonSerializer.Serialize(tokens, SerializerOptions);

        await _fileSystem.CreateDirectoryAsync(_directory, cancellationToken).ConfigureAwait(false);
        // Written aside then renamed: a process stopped halfway never costs the tokens already there.
        var partial = $"{_directory}/{key}{PartialSuffix}";
        await _fileSystem.WriteAllTextAsync(partial, json, cancellationToken).ConfigureAwait(false);
        await _fileSystem.MoveAsync(partial, path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        if (!await _fileSystem.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
            return false;
        return await _fileSystem.DeleteAsync(path, recursive: false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAccountAsync(string account, string? keptKey, CancellationToken cancellationToken)
    {
        _ = PathOf(account);
        var found = new List<string>();
        if (await _fileSystem.ExistsAsync(_directory, cancellationToken).ConfigureAwait(false))
        {
            await foreach (var entry in _fileSystem.EnumerateFilesAsync(_directory, new VirtualEnumerationOptions(Recursive: false), cancellationToken).ConfigureAwait(false))
            {
                if (entry.Kind == VirtualEntryKind.File && KeyOf(entry.VirtualPath) is { } key && key != keptKey && IsOfAccount(key, account))
                    found.Add(entry.VirtualPath);
            }
        }

        var deleted = false;
        foreach (var path in found)
            deleted |= await _fileSystem.DeleteAsync(path, recursive: false, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    private static string? KeyOf(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.EndsWith(PartialSuffix, StringComparison.Ordinal))
            return name[..^PartialSuffix.Length];
        return name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : null;
    }

    private static bool IsOfAccount(string key, string account) =>
        key.Length == account.Length + 1 + DigestLength
        && key.StartsWith(account, StringComparison.OrdinalIgnoreCase)
        && key[account.Length] == '-'
        && key.AsSpan(account.Length + 1).IndexOfAnyExcept(Digits) < 0;

    private string PathOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') || key.StartsWith('.'))
            throw new ArgumentException("A token key holds letters, digits, '.', '_' and '-' only, and does not start with '.'.", nameof(key));
        return $"{_directory}/{key}{Suffix}";
    }
}
