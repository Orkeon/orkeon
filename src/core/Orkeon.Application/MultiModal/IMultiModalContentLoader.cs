using Orkeon.Domain.SharedKernel.ValueObjects.Content;

namespace Orkeon.Application.MultiModal;

/// <summary>
/// Loads multi-modal content parts from external sources (virtual file system) so they
/// can be attached to LLM chat messages and sent to vision-capable providers
/// (Anthropic, OpenAI). Port implemented in Infrastructure (<c>MultiModalContentLoader</c>)
/// and registered by the opt-in extension <c>AddOrkeonMultiModal(...)</c> (R3.9).
/// </summary>
public interface IMultiModalContentLoader
{
    /// <summary>
    /// Loads an image file from the virtual file system, validates it against the configured
    /// multi-modal constraints (size, format), and returns the image content part carrying
    /// the raw bytes (sent base64-encoded to providers) and the inferred media type.
    /// </summary>
    /// <param name="virtualPath">The virtual path of the image file (e.g. <c>/workspace/chart.png</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated image content part.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the file extension or media type maps to no supported/allowed image format.
    /// </exception>
    /// <exception cref="FileNotFoundException">Thrown when the virtual path points to no file.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when multi-modal support is disabled or the image violates the configured constraints.
    /// </exception>
    System.Threading.Tasks.Task<ImageContentPart> LoadImageAsync(
        string virtualPath,
        CancellationToken cancellationToken = default);
}
