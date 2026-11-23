using System.Security;
using System.Text;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Chat.Auth;

/// <summary>
/// Reads the operator's credential file of an application account: the certificate with its key,
/// or the federated token its issuer rotates. Nothing it throws says the path or the content.
/// </summary>
internal static class ChatCredentialFile
{
    private const int MaxBytes = 64 * 1024;

    /// <summary>The text of <paramref name="path"/>, read again at each call. <paramref name="variable"/> is how a refusal names the file.</summary>
    // OUT-OF-SCOPE: an operator's credential of the host, named by an environment variable and rotated by
    // the host (a Kubernetes projected token, a key mounted by the platform). It is under no VFS mount, and
    // must not be: a mount would let the agents' file tools reach the key.
    [SuppressVfsCompliance("OUT-OF-SCOPE: the operator's own credential file, named by an environment variable; mounting it in the VFS would expose the key to the agents' file tools")]
    public static async Task<string> ReadAsync(string path, string variable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(variable);
        if (!Path.IsPathFullyQualified(path))
            throw Refuse(variable, "is not an absolute path");

        string failure;
        try
        {
            // A pipe would hold the opening until someone writes in it, and a device has no length either.
            var named = new FileInfo(path);
            if ((named.ResolveLinkTarget(returnFinalTarget: true) ?? named) is FileInfo { Exists: true, Length: 0 })
                throw Refuse(variable, "names an empty file, or one that is not a regular file");

            // A device would never end: the read stops at a size no credential has.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!file.CanSeek)
                throw Refuse(variable, "names an empty file, or one that is not a regular file");

            var buffer = new byte[MaxBytes + 1];
            var length = await file.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (length <= MaxBytes)
            {
                using var bytes = new MemoryStream(buffer, 0, length);
                using var text = new StreamReader(bytes, Encoding.UTF8);
                return await text.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            failure = "names a file too large for a credential";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            failure = "names a file that does not exist";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            failure = "names a file that cannot be read";
        }

        // Thrown outside the catch: the system's own message holds the path.
        throw Refuse(variable, failure);
    }

    private static ChatToolException Refuse(string variable, string failure) =>
        new(ChatErrorCode.InvalidConfiguration, $"The environment variable {variable} {failure}.");
}
