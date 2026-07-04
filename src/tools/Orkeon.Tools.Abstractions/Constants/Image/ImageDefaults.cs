namespace Orkeon.Tools.Abstractions.Constants.Image;

/// <summary>
/// Magic byte signatures and MIME type constants for supported image formats.
/// </summary>
internal static class ImageDefaults
{
    /// <summary>Number of bytes required to identify an image format from its header.</summary>
    public const int MagicByteLength = 4;

    // ── Magic bytes ────────────────────────────────────────────────────

    /// <summary>PNG file signature: first 4 bytes (0x89 0x50 0x4E 0x47).</summary>
    public static readonly (byte B0, byte B1, byte B2, byte B3) PngMagicBytes = (0x89, 0x50, 0x4E, 0x47);

    /// <summary>JPEG file signature: first 3 bytes (0xFF 0xD8 0xFF).</summary>
    public static readonly (byte B0, byte B1, byte B2) JpegMagicBytes = (0xFF, 0xD8, 0xFF);

    /// <summary>GIF file signature: first 4 bytes (0x47 0x49 0x46 0x38).</summary>
    public static readonly (byte B0, byte B1, byte B2, byte B3) GifMagicBytes = (0x47, 0x49, 0x46, 0x38);

    /// <summary>WebP file signature: first 4 bytes of the RIFF header (0x52 0x49 0x46 0x46).</summary>
    public static readonly (byte B0, byte B1, byte B2, byte B3) WebpMagicBytes = (0x52, 0x49, 0x46, 0x46);

    // ── MIME types ─────────────────────────────────────────────────────

    /// <summary>MIME type for PNG images.</summary>
    public const string MimePng = "image/png";

    /// <summary>MIME type for JPEG images.</summary>
    public const string MimeJpeg = "image/jpeg";

    /// <summary>MIME type for GIF images.</summary>
    public const string MimeGif = "image/gif";

    /// <summary>MIME type for WebP images.</summary>
    public const string MimeWebp = "image/webp";

    /// <summary>MIME type for SVG images.</summary>
    public const string MimeSvg = "image/svg+xml";

    /// <summary>Fallback MIME type for unrecognized binary content.</summary>
    public const string MimeOctetStream = "application/octet-stream";
}
