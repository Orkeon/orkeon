using Microsoft.Extensions.Options;
using Orkeon.Application.MultiModal;
using Orkeon.Application.Validation;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs.Converters;

namespace Orkeon.Infrastructure.MultiModal;

/// <summary>
/// Default <see cref="IMultiModalContentLoader"/> implementation (R3.9 — real vision wiring).
/// Reads image files through the virtual file system (<see cref="IFileSystemService"/>,
/// mount-aware and rights-audited), infers the media type from the file extension via
/// <see cref="ContentConverter.InferImageMediaType"/>, and validates the result against the
/// configured <see cref="MultiModalOptions"/> through <see cref="IContentValidationService"/>.
/// </summary>
public sealed class MultiModalContentLoader : IMultiModalContentLoader
{
    private readonly IFileSystemService _fileSystem;
    private readonly IContentValidationService _validator;
    private readonly MultiModalOptions _options;

    /// <summary>Initializes a new instance of <see cref="MultiModalContentLoader"/>.</summary>
    /// <param name="fileSystem">The virtual file system service.</param>
    /// <param name="validator">The multi-modal content validation service.</param>
    /// <param name="options">The multi-modal options (size/format constraints).</param>
    public MultiModalContentLoader(
        IFileSystemService fileSystem,
        IContentValidationService validator,
        IOptions<MultiModalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(options);
        _fileSystem = fileSystem;
        _validator = validator;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<ImageContentPart> LoadImageAsync(
        string virtualPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        if (!_options.Enabled)
        {
            throw new InvalidOperationException(
                "Multi-modal content support is disabled (Orkeon:MultiModal:Enabled = false).");
        }

        var image = await ContentConverter
            .LoadImageAsync(_fileSystem, virtualPath, cancellationToken)
            .ConfigureAwait(false);

        var validation = _validator.Validate(MultiModalContent.Empty().AddImage(image));
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Image '{virtualPath}' failed multi-modal validation: {string.Join("; ", validation.Errors)}.");
        }

        return image;
    }
}
