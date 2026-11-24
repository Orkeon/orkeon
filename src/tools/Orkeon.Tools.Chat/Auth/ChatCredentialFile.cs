using System.Security;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Chat.Auth;

/// <summary>
/// Reads the operator's credential file of an application account: the certificate with its key,
/// or the federated token its issuer rotates. The host mounts the folder of each such file as an
/// internal root, which no agent-facing tool resolves, and the file is read through that mount.
/// Nothing it throws says the path or the content.
/// </summary>
internal sealed class ChatCredentialFile
{
    private const int MaxBytes = 64 * 1024;
    private const int MaxLinks = 8;
    private const string Unreadable = "names a file that cannot be read";
    private const string Irregular = "names an empty file, or one that is not a regular file";

    private readonly IFileSystemService _fileSystem;
    private readonly string _root;

    /// <summary>Creates the reader over <paramref name="virtualRoot"/> of <paramref name="fileSystem"/>, the host's privileged view.</summary>
    public ChatCredentialFile(IFileSystemService fileSystem, string virtualRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRoot);
        _fileSystem = fileSystem;
        _root = virtualRoot.TrimEnd('/');
    }

    /// <summary>What a host that mounts no credential file answers for the file <paramref name="variable"/> names.</summary>
    public static ChatToolException Unmounted(string variable) =>
        Refuse(variable, "names a file whose folder the host did not mount when it started: its start says why, when it is an orkeon runner or the REPL");

    /// <summary>
    /// The folder, under the host's root, the directory of <paramref name="path"/> is mounted at; null when
    /// <paramref name="path"/> is not an absolute path of a file. The host and the reader compute it alike.
    /// </summary>
    public static string? Folder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Directory(path) is { } directory
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(directory)))[..16]
            : null;
    }

    /// <summary>The directory holding <paramref name="path"/>; null when it is not an absolute path of a file.</summary>
    public static string? Directory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (!Path.IsPathFullyQualified(path) || Path.GetFileName(path).Length == 0)
                return null;
            return Path.GetDirectoryName(path) is { Length: > 0 } directory ? directory : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The text of <paramref name="path"/>, read again at each call. <paramref name="variable"/> is how a refusal names the file.</summary>
    public async Task<string> ReadAsync(string path, string variable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(variable);
        if (!Path.IsPathFullyQualified(path))
            throw Refuse(variable, "is not an absolute path");
        if (Folder(path) is not { } folder)
            throw Refuse(variable, Unreadable);

        string failure;
        try
        {
            var (file, problem) = await FindAsync(_fileSystem, $"{_root}/{folder}/{Path.GetFileName(path)}", cancellationToken).ConfigureAwait(false);
            if (file is null)
                throw Refuse(variable, problem);

            var stream = await _fileSystem.OpenReadStreamAsync(file, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                // The size may have changed since it was read: the read stops at one no credential has.
                var buffer = new byte[MaxBytes + 1];
                var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (length <= MaxBytes)
                {
                    using var bytes = new MemoryStream(buffer, 0, length);
                    using var text = new StreamReader(bytes, Encoding.UTF8);
                    return await text.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            failure = "names a file too large for a credential";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            failure = "names a file that does not exist";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException or FileAccessDeniedException)
        {
            failure = Unreadable;
        }

        // Thrown outside the catch: the system's own message holds the path.
        throw Refuse(variable, failure);
    }

    /// <summary>
    /// The regular file <paramref name="virtualPath"/> names, its links followed inside the mounted folders, or
    /// why there is none. A pipe would hold the opening until someone writes in it: like a device, it has no
    /// length, and is refused before it is opened.
    /// </summary>
    private async Task<(string? File, string Problem)> FindAsync(IFileSystemService fileSystem, string virtualPath, CancellationToken cancellationToken)
    {
        for (var hop = 0; hop <= MaxLinks; hop++)
        {
            if (!fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read).IsAllowed)
                return (null, "names a file whose folder the host did not mount when it started, or a link that leaves that folder");
            var entry = await fileSystem.TryGetEntryAsync(virtualPath, cancellationToken).ConfigureAwait(false);
            switch (entry)
            {
                case null:
                    return (null, "names a file that does not exist");
                case { Kind: VirtualEntryKind.File, SizeBytes: 0 }:
                    return (null, Irregular);
                case { Kind: VirtualEntryKind.File, SizeBytes: > MaxBytes }:
                    return (null, "names a file too large for a credential");
                case { Kind: VirtualEntryKind.File }:
                    return (virtualPath, string.Empty);
                case { Kind: VirtualEntryKind.SymLink, LinkTarget: { } target } when target.StartsWith(_root + "/", StringComparison.Ordinal):
                    virtualPath = target;
                    break;
                default:
                    return (null, Unreadable);
            }
        }

        return (null, Unreadable);
    }

    private static ChatToolException Refuse(string variable, string failure) =>
        new(ChatErrorCode.InvalidConfiguration, $"The environment variable {variable} {failure}.");
}

/// <summary>The reader of credential files a host registered, or none: a file is then refused.</summary>
/// <param name="Reader">The reader, or null in a host that mounts no credential file.</param>
internal sealed record ChatCredentialFileSource(ChatCredentialFile? Reader);
