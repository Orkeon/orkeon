using MimeKit;

namespace Orkeon.Tools.Email.Mime;

/// <summary>The safe names of an e-mail's attachments: <see cref="SafeFileNames"/>, with the extension of their MIME type.</summary>
internal static class AttachmentNames
{
    /// <summary>
    /// A safe file name for the attachment at <paramref name="index"/> (zero-based), falling back
    /// to <c>attachment-N</c> plus an extension guessed from <paramref name="contentType"/>.
    /// </summary>
    public static string Sanitize(string? declaredName, int index, ContentType? contentType) =>
        SafeFileNames.Sanitize(
            declaredName,
            index,
            contentType is not null && MimeTypes.TryGetExtension(contentType.MimeType, out var known) ? known : ".bin");

    /// <inheritdoc cref="SafeFileNames.Unique"/>
    public static string Unique(string name, ISet<string> taken) => SafeFileNames.Unique(name, taken);
}
