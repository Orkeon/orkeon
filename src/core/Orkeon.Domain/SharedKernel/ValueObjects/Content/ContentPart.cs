namespace Orkeon.Domain.SharedKernel.ValueObjects.Content;

/// <summary>
/// Defines the type of content in a multi-modal message part.
/// </summary>
public sealed record ContentType
{
    /// <summary>Gets the string value of this content type.</summary>
    public string Value { get; }
    private ContentType(string value) => Value = value;

    /// <summary>Plain text content.</summary>
    public static readonly ContentType Text = new("Text");
    /// <summary>Image content (URI or raw bytes).</summary>
    public static readonly ContentType Image = new("Image");
    /// <summary>Audio content (URI or raw bytes).</summary>
    public static readonly ContentType Audio = new("Audio");
    /// <summary>File attachment content.</summary>
    public static readonly ContentType File = new("File");
    /// <summary>Tool execution result content.</summary>
    public static readonly ContentType ToolResult = new("ToolResult");

    private static readonly Dictionary<string, ContentType> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Text)] = Text,
        [nameof(Image)] = Image,
        [nameof(Audio)] = Audio,
        [nameof(File)] = File,
        [nameof(ToolResult)] = ToolResult,
    };

    /// <summary>Gets all valid content types.</summary>
    public static IReadOnlyCollection<ContentType> All => s_all.Values;

    /// <summary>Creates a <see cref="ContentType"/> from its string representation.</summary>
    public static ContentType From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown ContentType: '{value}'", nameof(value));

    /// <summary>Attempts to create a <see cref="ContentType"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out ContentType? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(ContentType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}

/// <summary>
/// Base record for all content parts in a multi-modal message.
/// </summary>
public abstract record ContentPart
{
    /// <summary>Gets the content type.</summary>
    public ContentType Type { get; init; } = ContentType.Text;
    /// <summary>Gets the optional MIME type.</summary>
    public string? MimeType { get; init; }
}

/// <summary>
/// Represents a text content part.
/// </summary>
public sealed record TextContentPart : ContentPart
{
    /// <summary>Gets the text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Initializes a new empty <see cref="TextContentPart"/>.</summary>
    public TextContentPart() => Type = ContentType.Text;

    /// <summary>Initializes a new <see cref="TextContentPart"/> with the given text.</summary>
    /// <param name="text">The text content.</param>
    public TextContentPart(string text) : this()
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }
}

/// <summary>
/// Represents an image content part, either from a URI or raw bytes.
/// </summary>
public sealed record ImageContentPart : ContentPart
{
    /// <summary>Gets the image URI, or <see langword="null"/> if using raw bytes.</summary>
    public Uri? Uri { get; init; }
    /// <summary>Gets the raw image data, or <see langword="null"/> if using a URI.</summary>
    public IReadOnlyList<byte>? Data { get; init; }
    /// <summary>Gets the optional alt text description.</summary>
    public string? AltText { get; init; }

    /// <summary>Initializes a new empty <see cref="ImageContentPart"/>.</summary>
    public ImageContentPart() => Type = ContentType.Image;

    /// <summary>Creates an image content part from a URI.</summary>
    /// <param name="uri">The image URI.</param>
    /// <param name="mimeType">The optional MIME type.</param>
    /// <returns>An <see cref="ImageContentPart"/> from the URI.</returns>
    public static ImageContentPart FromUri(Uri uri, string? mimeType = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new() { Uri = uri, MimeType = mimeType ?? "image/png" };
    }

    /// <summary>Creates an image content part from a base64-encoded string.</summary>
    /// <param name="base64">The base64-encoded image data.</param>
    /// <param name="mimeType">The MIME type.</param>
    /// <returns>An <see cref="ImageContentPart"/> from the base64 data.</returns>
    public static ImageContentPart FromBase64(string base64, string mimeType = "image/png")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return new() { Data = Convert.FromBase64String(base64), MimeType = mimeType };
    }

    /// <summary>Creates an image content part from raw bytes.</summary>
    /// <param name="data">The raw image bytes.</param>
    /// <param name="mimeType">The MIME type.</param>
    /// <returns>An <see cref="ImageContentPart"/> from raw bytes.</returns>
    public static ImageContentPart FromBytes(byte[] data, string mimeType = "image/png")
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("Image data cannot be empty.", nameof(data));
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return new() { Data = data, MimeType = mimeType };
    }
}

/// <summary>
/// Represents an audio content part, either from a URI or raw bytes.
/// </summary>
public sealed record AudioContentPart : ContentPart
{
    /// <summary>Gets the audio URI, or <see langword="null"/> if using raw bytes.</summary>
    public Uri? Uri { get; init; }
    /// <summary>Gets the raw audio data, or <see langword="null"/> if using a URI.</summary>
    public IReadOnlyList<byte>? Data { get; init; }
    /// <summary>Gets the optional audio duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Initializes a new empty <see cref="AudioContentPart"/>.</summary>
    public AudioContentPart() => Type = ContentType.Audio;

    /// <summary>Creates an audio content part from a URI.</summary>
    /// <param name="uri">The audio URI.</param>
    /// <param name="mimeType">The optional MIME type.</param>
    /// <returns>An <see cref="AudioContentPart"/> from the URI.</returns>
    public static AudioContentPart FromUri(Uri uri, string? mimeType = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new() { Uri = uri, MimeType = mimeType ?? "audio/wav" };
    }

    /// <summary>Creates an audio content part from raw bytes.</summary>
    /// <param name="data">The raw audio bytes.</param>
    /// <param name="mimeType">The MIME type.</param>
    /// <returns>An <see cref="AudioContentPart"/> from raw bytes.</returns>
    public static AudioContentPart FromBytes(byte[] data, string mimeType = "audio/wav")
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("Audio data cannot be empty.", nameof(data));
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return new() { Data = data, MimeType = mimeType };
    }
}

/// <summary>
/// Represents a file content part for arbitrary file attachments.
/// </summary>
public sealed record FileContentPart : ContentPart
{
    /// <summary>Gets the file name.</summary>
    public string FileName { get; init; } = string.Empty;
    /// <summary>Gets the raw file data, or <see langword="null"/> if using a URI.</summary>
    public IReadOnlyList<byte>? Data { get; init; }
    /// <summary>Gets the optional file URI.</summary>
    public Uri? Uri { get; init; }

    /// <summary>Initializes a new empty <see cref="FileContentPart"/>.</summary>
    public FileContentPart() => Type = ContentType.File;
}
