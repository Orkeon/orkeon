using System.Buffers.Binary;
using System.Globalization;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// What a vendor documents of the images it takes (sources: docs/guides/multimodal.md, "The images of a run"). The bound left
/// null is one no vendor page states; a provider missing from the table gets the prudent size alone.
/// </summary>
internal sealed record RunImageLimits
{
    /// <summary>What a request keeps for its text when the vendor bounds the whole of it, in bytes.</summary>
    private const long RestOfTheRequest = 1_000_000;

    /// <summary>More than the prefix of a data URI takes, where the vendor counts it.</summary>
    private const long DataUriPrefix = 64;

    /// <summary>The bound of a provider that documents none: 5 MB of base64.</summary>
    private static readonly RunImageLimits s_prudent = new() { MaxBytes = Decoded(5_000_000) };

    private static readonly Dictionary<string, RunImageLimits> s_byProvider = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic"] = new() { MaxBytes = Decoded(10_000_000), MaxRequest = 128_000_000, MaxSide = 8000 },
        ["azure-openai"] = new() { MaxBytes = 20_000_000 },
        ["gemini"] = new() { MaxRequest = 20_000_000, Formats = ["image/png", "image/jpeg", "image/webp"] },
        ["grok"] = new() { MaxBytes = 20 * 1024 * 1024, Formats = ["image/png", "image/jpeg"] },
        ["kimi"] = new() { MaxRequest = 100_000_000 },
        ["minimax"] = new() { MaxBytes = 10_000_000, MaxRequest = 64_000_000, MaxRatio = 200 },
        ["mistral"] = new() { MaxBytes = 10_000_000, MaxImages = 8, MaxSide = 10_000, Formats = ["image/png", "image/jpeg", "image/webp"] },
        ["OpenAI"] = new() { MaxRequest = 512_000_000 },
        ["qwen"] = new()
        {
            MaxBytes = Decoded((50 * 1024 * 1024) - DataUriPrefix),
            MaxRequest = 16 * 1024 * 1024,
            MinSide = 11,
            MaxRatio = 200,
            MaxBox = (7680, 4320),
            Formats = ["image/png", "image/jpeg", "image/webp"],
            FormatsOfLarge = ((3840, 2160), ["image/png", "image/jpeg"]),
        },
        ["zai"] = new() { MaxBytes = 4_999_999, MaxSide = 6000, Formats = ["image/png", "image/jpeg"] },
    };

    /// <summary>The most bytes of one image.</summary>
    public long? MaxBytes { get; private init; }

    /// <summary>The most bytes of a whole request, its images in base64.</summary>
    public long? MaxRequest { get; private init; }

    /// <summary>The most pixels on a side.</summary>
    public long? MaxSide { get; private init; }

    /// <summary>The fewest pixels on a side.</summary>
    public long? MinSide { get; private init; }

    /// <summary>The most times the long side may hold the short one.</summary>
    public long? MaxRatio { get; private init; }

    /// <summary>The most images of a request.</summary>
    public int? MaxImages { get; private init; }

    /// <summary>The most pixels of the long side and of the short one.</summary>
    public (long Long, long Short)? MaxBox { get; private init; }

    /// <summary>The media types taken of an image that does not fit the box; null when they are the same.</summary>
    public ((long Long, long Short) Box, IReadOnlyList<string> Formats)? FormatsOfLarge { get; private init; }

    /// <summary>The media types taken; null when every one Orkeon sends is.</summary>
    public IReadOnlyList<string>? Formats { get; private init; }

    /// <summary>The limits of the provider named <paramref name="provider"/>.</summary>
    public static RunImageLimits Of(string provider) => s_byProvider.GetValueOrDefault(provider, s_prudent);

    /// <summary>Why the vendor would refuse these bytes; null when nothing it documents says so.</summary>
    public string? RefusalOf(ReadOnlySpan<byte> bytes, string mediaType)
    {
        if (Formats is not null && !Formats.Contains(mediaType))
            return $"its model's provider takes no {mediaType} image";
        if (SizeOf(bytes, mediaType) is not var (width, height))
            return null;

        var (shortSide, longSide) = (Math.Min(width, height), Math.Max(width, height));
        var allowed = longSide <= (MaxSide ?? long.MaxValue)
            && shortSide >= (MinSide ?? 0)
            && (MaxBox is not { } box || IsWithin(box))
            && (FormatsOfLarge is not { } large || IsWithin(large.Box) || large.Formats.Contains(mediaType))
            && (MaxRatio is not { } ratio || longSide <= shortSide * ratio);
        return allowed
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"its model's provider takes no {mediaType} image of {width} x {height} pixels");

        bool IsWithin((long Long, long Short) bounds) => longSide <= bounds.Long && shortSide <= bounds.Short;
    }

    /// <summary>Whether <paramref name="bytes"/> more still fit a request that carries <paramref name="encoded"/> bytes of base64.</summary>
    public bool Fits(long encoded, long bytes) => MaxRequest is not { } most || encoded + Encoded(bytes) <= most - RestOfTheRequest;

    /// <summary>The length in base64 of <paramref name="bytes"/> bytes.</summary>
    public static long Encoded(long bytes) => (bytes + 2) / 3 * 4;

    private static long Decoded(long encoded) => encoded / 4 * 3;

    /// <summary>The width and the height the header says; null when it does not say them.</summary>
    private static (long Width, long Height)? SizeOf(ReadOnlySpan<byte> bytes, string mediaType) => mediaType switch
    {
        "image/png" when bytes.Length >= 24 =>
            (BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadUInt32BigEndian(bytes[20..])),
        "image/gif" when bytes.Length >= 10 =>
            (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..])),
        "image/jpeg" => JpegSizeOf(bytes),
        "image/webp" when bytes.Length >= 30 => WebpSizeOf(bytes),
        _ => null,
    };

    private static (long Width, long Height)? JpegSizeOf(ReadOnlySpan<byte> bytes)
    {
        var at = 2;
        while (at + 9 <= bytes.Length && bytes[at] == 0xFF)
        {
            var marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                // A fill byte before the marker.
                at++;
                continue;
            }

            // The frame headers, less the three tables numbered among them.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]));
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            if (length < 2)
                return null;
            at += 2 + length;
        }

        return null;
    }

    private static (long Width, long Height)? WebpSizeOf(ReadOnlySpan<byte> bytes)
    {
        var kind = bytes[12..16];
        if (kind.SequenceEqual("VP8X"u8))
            return (1 + UInt24(bytes[24..]), 1 + UInt24(bytes[27..]));
        if (kind.SequenceEqual("VP8 "u8))
        {
            return (BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF);
        }

        if (!kind.SequenceEqual("VP8L"u8))
            return null;
        var packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
        return (1 + (packed & 0x3FFF), 1 + ((packed >> 14) & 0x3FFF));
    }

    private static long UInt24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
