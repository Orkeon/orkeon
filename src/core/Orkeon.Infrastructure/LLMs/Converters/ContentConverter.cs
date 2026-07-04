using Microsoft.Extensions.AI;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;

namespace Orkeon.Infrastructure.LLMs.Converters;

/// <summary>
/// Converts Orkeon domain <see cref="MultiModalContent"/> into provider-ready payload
/// fragments and Microsoft.Extensions.AI content types (R3.9 — real vision wiring).
///
/// <para>Three conversion families are provided:</para>
/// <list type="bullet">
/// <item><see cref="ToAnthropicContentBlocks"/> — Anthropic Messages API content blocks
/// (<c>{"type":"image","source":{"type":"base64"|"url",...}}</c>).</item>
/// <item><see cref="ToOpenAIContentParts"/> — OpenAI Chat Completions content parts
/// (<c>{"type":"image_url","image_url":{"url":...}}</c>, URL or base64 data URL).</item>
/// <item><see cref="ToAIContents"/>/<see cref="FromAIContents"/> — Microsoft.Extensions.AI
/// <see cref="TextContent"/>/<see cref="DataContent"/>/<see cref="UriContent"/> mapping.</item>
/// </list>
///
/// Unsupported part types or media types raise a <see cref="NotSupportedException"/> with an
/// explicit message instead of silently degrading to text.
/// </summary>
public static class ContentConverter
{
    private const string DefaultImageMediaType = "image/png";
    private const string DefaultAudioMediaType = "audio/wav";
    private const string DefaultBinaryMediaType = "application/octet-stream";
    private const string Base64DataUrlSuffix = ";base64";

    /// <summary>
    /// Image media types accepted by both the Anthropic Messages API and the
    /// OpenAI Chat Completions vision endpoint.
    /// </summary>
    private static readonly string[] s_supportedImageMediaTypes =
    [
        "image/png", "image/jpeg", "image/gif", "image/webp"
    ];

    // ─────────────────────────────────────────────────────────────
    //  Provider payload conversions (Anthropic / OpenAI)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="MultiModalContent"/> into Anthropic Messages API content blocks.
    /// Text parts map to <c>text</c> blocks; image parts map to <c>image</c> blocks with a
    /// <c>base64</c> source (raw bytes or <c>data:</c> URL) or a <c>url</c> source (http/https URL).
    /// </summary>
    /// <param name="content">The multi-modal content to convert.</param>
    /// <returns>The ordered list of Anthropic content block objects, ready for JSON serialization.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown for audio/file parts, unsupported image media types, or unsupported URI schemes.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when an image part carries neither data nor URI.</exception>
    public static IReadOnlyList<object> ToAnthropicContentBlocks(MultiModalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = new List<object>(content.Parts.Count);
        foreach (var part in content.Parts)
        {
            blocks.Add(part switch
            {
                TextContentPart text => new { type = "text", text = text.Text },
                ImageContentPart image => ToAnthropicImageBlock(image),
                _ => throw UnsupportedPart(part, "Anthropic Messages API")
            });
        }
        return blocks;
    }

    /// <summary>
    /// Converts a <see cref="MultiModalContent"/> into OpenAI Chat Completions content parts.
    /// Text parts map to <c>text</c> parts; image parts map to <c>image_url</c> parts carrying
    /// either the original http(s)/data URL or a base64 <c>data:</c> URL built from raw bytes.
    /// </summary>
    /// <param name="content">The multi-modal content to convert.</param>
    /// <returns>The ordered list of OpenAI content part objects, ready for JSON serialization.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown for audio/file parts, unsupported image media types, or unsupported URI schemes.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when an image part carries neither data nor URI.</exception>
    public static IReadOnlyList<object> ToOpenAIContentParts(MultiModalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parts = new List<object>(content.Parts.Count);
        foreach (var part in content.Parts)
        {
            parts.Add(part switch
            {
                TextContentPart text => new { type = "text", text = text.Text },
                ImageContentPart image => ToOpenAIImagePart(image),
                _ => throw UnsupportedPart(part, "OpenAI Chat Completions API")
            });
        }
        return parts;
    }

    private static object ToAnthropicImageBlock(ImageContentPart image)
    {
        if (image.Data is { Count: > 0 })
        {
            var mediaType = ValidateImageMediaType(image.MimeType ?? DefaultImageMediaType);
            return new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = mediaType,
                    data = Convert.ToBase64String(AsByteArray(image.Data))
                }
            };
        }

        if (image.Uri is { } uri)
        {
            if (IsHttpScheme(uri))
            {
                return new
                {
                    type = "image",
                    source = new { type = "url", url = uri.AbsoluteUri }
                };
            }

            if (TryParseBase64DataUrl(uri, out var mediaType, out var base64Data))
            {
                ValidateImageMediaType(mediaType);
                return new
                {
                    type = "image",
                    source = new { type = "base64", media_type = mediaType, data = base64Data }
                };
            }

            throw UnsupportedImageUri(uri, "Anthropic Messages API");
        }

        throw MissingImagePayload();
    }

    private static object ToOpenAIImagePart(ImageContentPart image)
    {
        if (image.Data is { Count: > 0 })
        {
            var mediaType = ValidateImageMediaType(image.MimeType ?? DefaultImageMediaType);
            var dataUrl = $"data:{mediaType}{Base64DataUrlSuffix},{Convert.ToBase64String(AsByteArray(image.Data))}";
            return new { type = "image_url", image_url = new { url = dataUrl } };
        }

        if (image.Uri is { } uri)
        {
            if (IsHttpScheme(uri))
                return new { type = "image_url", image_url = new { url = uri.AbsoluteUri } };

            if (TryParseBase64DataUrl(uri, out var mediaType, out _))
            {
                ValidateImageMediaType(mediaType);
                return new { type = "image_url", image_url = new { url = uri.OriginalString } };
            }

            throw UnsupportedImageUri(uri, "OpenAI Chat Completions API");
        }

        throw MissingImagePayload();
    }

    // ─────────────────────────────────────────────────────────────
    //  Image loading from the virtual file system
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads an image file from the virtual file system and converts it into an
    /// <see cref="ImageContentPart"/> carrying the raw bytes (sent base64-encoded to
    /// providers) and the media type inferred from the file extension.
    /// </summary>
    /// <param name="fileSystem">The virtual file system service (mount-aware, rights-audited).</param>
    /// <param name="virtualPath">The virtual path of the image file (e.g. <c>/workspace/chart.png</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image content part built from the file bytes.</returns>
    /// <exception cref="NotSupportedException">Thrown when the file extension maps to no supported image format.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the virtual path points to no file.</exception>
    /// <exception cref="Orkeon.Domain.FileSystem.FileAccessDeniedException">
    /// Thrown when the path is outside any mount or lacks Read rights.
    /// </exception>
    public static Task<ImageContentPart> LoadImageAsync(
        IFileSystemService fileSystem,
        string virtualPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        return LoadImageCoreAsync();

        async Task<ImageContentPart> LoadImageCoreAsync()
        {
            var mediaType = InferImageMediaType(virtualPath);
            var bytes = await fileSystem.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException(
                    $"Image file not found at virtual path '{virtualPath}'.", virtualPath);

            return ImageContentPart.FromBytes(bytes, mediaType);
        }
    }

    /// <summary>
    /// Infers the image media type from a file name or path extension.
    /// </summary>
    /// <param name="fileName">The file name or path (virtual or relative).</param>
    /// <returns>The inferred media type (e.g. <c>image/png</c>).</returns>
    /// <exception cref="NotSupportedException">Thrown when the extension maps to no supported image format.</exception>
    public static string InferImageMediaType(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Path.GetExtension is pure string manipulation — no filesystem I/O involved.
        var extension = Path.GetExtension(fileName);
#pragma warning disable CA1308 // lowercase is the normalized extension token driving the switch/media-type mapping, not a comparison normalization
        return extension.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => throw new NotSupportedException(
                $"Unsupported image file extension '{extension}' for '{fileName}'. " +
                "Supported extensions: .png, .jpg, .jpeg, .gif, .webp.")
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Microsoft.Extensions.AI conversions
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a MultiModalContent to a list of AIContent items for use with IChatClient.
    /// Text parts map to <see cref="TextContent"/>; image/audio/file parts map to
    /// <see cref="DataContent"/> (raw bytes) or <see cref="UriContent"/> (URI reference).
    /// </summary>
    /// <param name="content">The multi-modal content to convert.</param>
    /// <returns>The ordered list of AIContent items.</returns>
    /// <exception cref="ArgumentException">Thrown when a binary part carries neither data nor URI.</exception>
    public static IList<AIContent> ToAIContents(MultiModalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = new List<AIContent>(content.Parts.Count);
        foreach (var part in content.Parts)
        {
            result.Add(part switch
            {
                TextContentPart text => new TextContent(text.Text),
                ImageContentPart img => ToBinaryAIContent(img.Data, img.Uri, img.MimeType ?? DefaultImageMediaType),
                AudioContentPart audio => ToBinaryAIContent(audio.Data, audio.Uri, audio.MimeType ?? DefaultAudioMediaType),
                FileContentPart file => ToFileAIContent(file),
                _ => new TextContent(part.ToString() ?? string.Empty)
            });
        }
        return result;
    }

    /// <summary>
    /// Converts a list of AIContent items back to a MultiModalContent.
    /// <see cref="TextContent"/> maps to text parts; <see cref="DataContent"/> and
    /// <see cref="UriContent"/> map to image, audio, or file parts based on their media type.
    /// </summary>
    /// <param name="contents">The AIContent items to convert.</param>
    /// <returns>The reconstructed multi-modal content.</returns>
    public static MultiModalContent FromAIContents(IEnumerable<AIContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var result = MultiModalContent.Empty();
        foreach (var content in contents)
        {
            result = content switch
            {
                TextContent text => result.AddText(text.Text),
                DataContent data => AppendDataContent(result, data),
                UriContent uri => AppendUriContent(result, uri),
                _ => result
            };
        }
        return result;
    }

    /// <summary>
    /// Creates a ChatMessage from a role string and MultiModalContent.
    /// </summary>
    public static ChatMessage ToChatMessage(string role, MultiModalContent content)
    {
        return new ChatMessage(MapRole(role), ToAIContents(content));
    }

    /// <summary>
    /// Extracts the role and content from a ChatMessage.
    /// </summary>
    public static (string Role, MultiModalContent Content) FromChatMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (message.Role.Value, FromAIContents(message.Contents));
    }

    private static ChatRole MapRole(string role) => role switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User
    };

    private static byte[] AsByteArray(IReadOnlyList<byte> data) =>
        data as byte[] ?? [.. data];

    private static AIContent ToBinaryAIContent(IReadOnlyList<byte>? data, Uri? uri, string mediaType)
    {
        if (data is { Count: > 0 })
            return new DataContent(AsByteArray(data), mediaType);
        if (uri is not null)
            return new UriContent(uri, mediaType);
        throw new ArgumentException(
            $"Content part with media type '{mediaType}' must carry either raw data or a URI.");
    }

    private static AIContent ToFileAIContent(FileContentPart file)
    {
        var mediaType = file.MimeType ?? DefaultBinaryMediaType;
        if (file.Data is { Count: > 0 })
        {
            return new DataContent(AsByteArray(file.Data), mediaType)
            {
                Name = string.IsNullOrEmpty(file.FileName) ? null : file.FileName
            };
        }
        if (file.Uri is not null)
            return new UriContent(file.Uri, mediaType);
        throw new ArgumentException(
            $"File content part '{file.FileName}' must carry either raw data or a URI.");
    }

    private static MultiModalContent AppendDataContent(MultiModalContent result, DataContent data)
    {
        if (data.HasTopLevelMediaType("image"))
            return result.AddImage(ImageContentPart.FromBytes(data.Data.ToArray(), data.MediaType));
        if (data.HasTopLevelMediaType("audio"))
            return result.AddAudio(AudioContentPart.FromBytes(data.Data.ToArray(), data.MediaType));
        return result.AddFile(new FileContentPart
        {
            FileName = data.Name ?? string.Empty,
            Data = data.Data.ToArray(),
            MimeType = data.MediaType
        });
    }

    private static MultiModalContent AppendUriContent(MultiModalContent result, UriContent uri)
    {
        if (uri.HasTopLevelMediaType("image"))
            return result.AddImage(ImageContentPart.FromUri(uri.Uri, uri.MediaType));
        if (uri.HasTopLevelMediaType("audio"))
            return result.AddAudio(AudioContentPart.FromUri(uri.Uri, uri.MediaType));
        return result.AddFile(new FileContentPart { Uri = uri.Uri, MimeType = uri.MediaType });
    }

    // ─────────────────────────────────────────────────────────────
    //  Shared helpers
    // ─────────────────────────────────────────────────────────────

    private static bool IsHttpScheme(Uri uri) =>
        uri.IsAbsoluteUri &&
        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Parses a base64 <c>data:</c> URL of the form <c>data:{mediaType};base64,{data}</c>.
    /// </summary>
    private static bool TryParseBase64DataUrl(Uri uri, out string mediaType, out string base64Data)
    {
        mediaType = string.Empty;
        base64Data = string.Empty;

        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
            return false;

        var raw = uri.OriginalString;
        var commaIndex = raw.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex < 0)
            return false;

        const string prefix = "data:";
        var header = raw[prefix.Length..commaIndex];
        if (!header.EndsWith(Base64DataUrlSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        mediaType = header[..^Base64DataUrlSuffix.Length];
        base64Data = raw[(commaIndex + 1)..];
        return mediaType.Length > 0 && base64Data.Length > 0;
    }

    private static string ValidateImageMediaType(string mediaType)
    {
        if (!s_supportedImageMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Image media type '{mediaType}' is not supported by vision-capable providers. " +
                $"Supported media types: {string.Join(", ", s_supportedImageMediaTypes)}.");
        }
        return mediaType;
    }

    private static NotSupportedException UnsupportedPart(ContentPart part, string apiName) =>
        new($"Content part of type '{part.Type.Value}' is not supported by the {apiName}. " +
            "Supported part types: Text, Image.");

    private static NotSupportedException UnsupportedImageUri(Uri uri, string apiName) =>
        new($"Image URI scheme '{uri.Scheme}' is not supported by the {apiName}. " +
            "Use an http(s) URL, a base64 'data:' URL, or provide raw image bytes.");

    private static ArgumentException MissingImagePayload() =>
        new("Image content part must carry either raw data (Data) or a URI (Uri).");
}
