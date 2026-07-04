using Microsoft.Extensions.Options;
using Orkeon.Application.Constants.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;

namespace Orkeon.Application.Validation;

/// <summary>
/// Validates multi-modal content against configured size, format, and duration limits.
/// Shared utility in Common/ — registered in Infrastructure (MultiModalServiceExtensions)
/// but not yet consumed by any Application feature folder. Awaiting adoption.
/// </summary>
public interface IContentValidationService
{
    /// <summary>
    /// Validates the given multi-modal content and returns a result indicating
    /// whether it meets the configured constraints.
    /// </summary>
    ContentValidationResult Validate(MultiModalContent content);
}

/// <summary>
/// Result of content validation, containing any validation errors.
/// </summary>
public record ContentValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Valid.
    /// </summary>
    public static ContentValidationResult Valid() => new(true, Array.Empty<string>());
}

/// <summary>
/// Configuration options for multi-modal content support.
/// </summary>
public class MultiModalOptions
{
    /// <summary>
    /// Whether multi-modal content is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum allowed image size in bytes (default: 20 MB).
    /// </summary>
    public long MaxImageSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed audio duration in seconds (default: 300 seconds / 5 minutes).
    /// </summary>
    public int MaxAudioDurationSeconds { get; set; } = ExecutionDefaults.DefaultMaxExecutionSeconds;

    /// <summary>
    /// List of supported image MIME types.
    /// </summary>
    public IReadOnlyList<string> SupportedImageFormats { get; init; } =
    [
        "image/png", "image/jpeg", "image/gif", "image/webp"
    ];

    /// <summary>
    /// List of supported audio MIME types.
    /// </summary>
    public IReadOnlyList<string> SupportedAudioFormats { get; init; } =
    [
        "audio/wav", "audio/mp3", "audio/ogg"
    ];

    /// <summary>
    /// Whether to automatically resize oversized images.
    /// </summary>
    public bool AutoResizeImages { get; set; } = true;

    /// <summary>
    /// Maximum image dimension (width or height) in pixels for auto-resize.
    /// </summary>
    public int MaxImageDimension { get; set; } = 2048;
}

/// <summary>
/// Validates multi-modal content parts against configured constraints.
/// </summary>
public sealed class ContentValidationService : IContentValidationService
{
    private readonly MultiModalOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ContentValidationService"/>.
    /// </summary>
    public ContentValidationService(IOptions<MultiModalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Validate.
    /// </summary>
    public ContentValidationResult Validate(MultiModalContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var errors = new List<string>();
        foreach (var part in content.Parts)
        {
            switch (part)
            {
                case ImageContentPart img:
                    ValidateImage(img, errors);
                    break;
                case AudioContentPart audio:
                    ValidateAudio(audio, errors);
                    break;
            }
        }
        return new ContentValidationResult(errors.Count == 0, errors);
    }

    private void ValidateImage(ImageContentPart img, List<string> errors)
    {
        if (img.Data != null && img.Data.Count > _options.MaxImageSizeBytes)
            errors.Add($"Image exceeds max size ({img.Data.Count} > {_options.MaxImageSizeBytes})");
        if (img.MimeType != null && !_options.SupportedImageFormats.Contains(img.MimeType))
            errors.Add($"Unsupported image format: {img.MimeType}");
    }

    private void ValidateAudio(AudioContentPart audio, List<string> errors)
    {
        if (audio.Duration > TimeSpan.FromSeconds(_options.MaxAudioDurationSeconds))
            errors.Add($"Audio exceeds max duration ({audio.Duration})");
        if (audio.MimeType != null && !_options.SupportedAudioFormats.Contains(audio.MimeType))
            errors.Add($"Unsupported audio format: {audio.MimeType}");
    }
}
