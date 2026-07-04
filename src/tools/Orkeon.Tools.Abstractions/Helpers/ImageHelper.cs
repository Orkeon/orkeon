using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Tools.Abstractions.Constants.Image;

namespace Orkeon.Tools.Abstractions.Helpers;

/// <summary>
/// Helper utilities for image detection, loading, and MIME type inference.
/// </summary>
public static class ImageHelper
{
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ImageDefaults.MimePng,
        ImageDefaults.MimeJpeg,
        ImageDefaults.MimeGif,
        ImageDefaults.MimeWebp,
        ImageDefaults.MimeSvg
    };

    /// <summary>
    /// Returns true if the given MIME type is a recognized image format.
    /// </summary>
    public static bool IsSupportedMimeType(string mimeType) => SupportedMimeTypes.Contains(mimeType);

    /// <summary>
    /// Detects the image MIME type from the file header (magic bytes).
    /// Returns "application/octet-stream" if the format is unrecognized.
    /// </summary>
    public static string DetectMimeType(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < ImageDefaults.MagicByteLength) return ImageDefaults.MimeOctetStream;
        return (data[0], data[1], data[2], data[3]) switch
        {
            var b when b == (ImageDefaults.PngMagicBytes.B0, ImageDefaults.PngMagicBytes.B1, ImageDefaults.PngMagicBytes.B2, ImageDefaults.PngMagicBytes.B3)
                => ImageDefaults.MimePng,
            var b when b.Item1 == ImageDefaults.JpegMagicBytes.B0 && b.Item2 == ImageDefaults.JpegMagicBytes.B1 && b.Item3 == ImageDefaults.JpegMagicBytes.B2
                => ImageDefaults.MimeJpeg,
            var b when b.Item1 == ImageDefaults.GifMagicBytes.B0 && b.Item2 == ImageDefaults.GifMagicBytes.B1 && b.Item3 == ImageDefaults.GifMagicBytes.B2
                => ImageDefaults.MimeGif,
            var b when b == (ImageDefaults.WebpMagicBytes.B0, ImageDefaults.WebpMagicBytes.B1, ImageDefaults.WebpMagicBytes.B2, ImageDefaults.WebpMagicBytes.B3)
                => ImageDefaults.MimeWebp,
            _ => ImageDefaults.MimeOctetStream
        };
    }

    /// <summary>
    /// Detects the MIME type from a file path extension.
    /// </summary>
    public static string DetectMimeTypeFromPath(string filePath)
    {
#pragma warning disable CA1308 // ext is normalized to the lowercase switch keys (".png", ".jpg", ...), a required match form, not a comparison normalization
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
#pragma warning restore CA1308
        return ext switch
        {
            ".png" => ImageDefaults.MimePng,
            ".jpg" or ".jpeg" => ImageDefaults.MimeJpeg,
            ".gif" => ImageDefaults.MimeGif,
            ".webp" => ImageDefaults.MimeWebp,
            ".svg" => ImageDefaults.MimeSvg,
            _ => ImageDefaults.MimeOctetStream
        };
    }

    /// <summary>
    /// Loads an image from a virtual file system path.
    /// </summary>
    /// <exception cref="FileNotFoundException">Thrown when the virtual path does not exist.</exception>
    public static Task<ImageContentPart> LoadFromVfsAsync(
        IFileSystemService fs, string vPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fs);
        return LoadFromVfsCoreAsync();

        async Task<ImageContentPart> LoadFromVfsCoreAsync()
        {
            var data = await fs.TryReadAllBytesAsync(vPath, ct).ConfigureAwait(false)
                       ?? throw new FileNotFoundException($"Virtual image file not found: {vPath}", vPath);
            var mimeType = DetectMimeType(data);
            return ImageContentPart.FromBytes(data, mimeType);
        }
    }

    /// <summary>
    /// Downloads an image from a URL via HTTP.
    /// </summary>
    public static Task<ImageContentPart> LoadFromUrlAsync(HttpClient httpClient, Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return LoadFromUrlCoreAsync();

        async Task<ImageContentPart> LoadFromUrlCoreAsync()
        {
            var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var mimeType = response.Content.Headers.ContentType?.MediaType ?? DetectMimeType(data);
            return ImageContentPart.FromBytes(data, mimeType);
        }
    }
}
