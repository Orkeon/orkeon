using System.Collections.Immutable;

namespace Orkeon.Domain.SharedKernel.ValueObjects.Content;

/// <summary>
/// Represents multi-modal content composed of one or more content parts (text, image, audio, file).
/// Immutable value object — each Add method returns a new instance.
/// </summary>
public sealed class MultiModalContent : IEquatable<MultiModalContent>
{
    private readonly ImmutableList<ContentPart> _parts;

    /// <summary>
    /// Gets the ordered list of content parts.
    /// </summary>
    public IReadOnlyList<ContentPart> Parts => _parts;

    /// <summary>
    /// Returns true if the content contains at least one text part.
    /// </summary>
    public bool HasText => _parts.Any(p => p is TextContentPart);

    /// <summary>
    /// Returns true if the content contains at least one image part.
    /// </summary>
    public bool HasImages => _parts.Any(p => p is ImageContentPart);

    /// <summary>
    /// Returns true if the content contains at least one audio part.
    /// </summary>
    public bool HasAudio => _parts.Any(p => p is AudioContentPart);

    /// <summary>
    /// Returns true if all parts are text parts.
    /// </summary>
    public bool IsTextOnly => _parts.Count > 0 && _parts.All(p => p is TextContentPart);

    /// <summary>Initializes an empty <see cref="MultiModalContent"/>.</summary>
    private MultiModalContent()
    {
        _parts = [];
    }

    /// <summary>Initializes a <see cref="MultiModalContent"/> with a single text part.</summary>
    /// <param name="text">The text content.</param>
    private MultiModalContent(string text)
    {
        _parts = ImmutableList.Create<ContentPart>(new TextContentPart(text));
    }

    private MultiModalContent(ImmutableList<ContentPart> parts)
    {
        _parts = parts;
    }

    /// <summary>Creates an empty <see cref="MultiModalContent"/>.</summary>
    /// <returns>An empty <see cref="MultiModalContent"/> instance.</returns>
    public static MultiModalContent Empty() => new();

    /// <summary>Creates a <see cref="MultiModalContent"/> from a single text string.</summary>
    /// <param name="text">The text content (must not be null).</param>
    /// <returns>A new <see cref="MultiModalContent"/> containing the text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    public static MultiModalContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(text);
    }

    /// <summary>Creates a <see cref="MultiModalContent"/> from the specified content parts.</summary>
    /// <param name="parts">The content parts to include.</param>
    /// <returns>A new <see cref="MultiModalContent"/> containing the parts.</returns>
    public static MultiModalContent Create(params ContentPart[] parts) =>
        new(ImmutableList.CreateRange(parts));

    /// <summary>
    /// Returns a new instance with a text part added.
    /// </summary>
    /// <param name="text">The text to add.</param>
    /// <returns>A new <see cref="MultiModalContent"/> with the text part appended.</returns>
    public MultiModalContent AddText(string text) =>
        new(_parts.Add(new TextContentPart(text)));

    /// <summary>
    /// Returns a new instance with an image part added.
    /// </summary>
    /// <param name="image">The image content part to add.</param>
    /// <returns>A new <see cref="MultiModalContent"/> with the image part appended.</returns>
    public MultiModalContent AddImage(ImageContentPart image) =>
        new(_parts.Add(image));

    /// <summary>
    /// Returns a new instance with an audio part added.
    /// </summary>
    /// <param name="audio">The audio content part to add.</param>
    /// <returns>A new <see cref="MultiModalContent"/> with the audio part appended.</returns>
    public MultiModalContent AddAudio(AudioContentPart audio) =>
        new(_parts.Add(audio));

    /// <summary>
    /// Returns a new instance with a file part added.
    /// </summary>
    /// <param name="file">The file content part to add.</param>
    /// <returns>A new <see cref="MultiModalContent"/> with the file part appended.</returns>
    public MultiModalContent AddFile(FileContentPart file) =>
        new(_parts.Add(file));

    /// <summary>
    /// Implicit conversion from string to a text-only MultiModalContent.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    public static implicit operator MultiModalContent(string text) => FromText(text);

    /// <summary>Friendly-named alternate for the implicit conversion from a string.</summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>A text-only <see cref="MultiModalContent"/>.</returns>
    public static MultiModalContent FromString(string text) => FromText(text);

    /// <summary>
    /// Extracts only the text parts and concatenates them with newlines.
    /// </summary>
    /// <returns>The concatenated text content.</returns>
    public string ToTextOnly() => string.Join("\n",
        _parts.OfType<TextContentPart>().Select(p => p.Text));

    /// <inheritdoc />
    public override string ToString() => ToTextOnly();

    /// <inheritdoc />
    public bool Equals(MultiModalContent? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_parts.Count != other._parts.Count) return false;
        return _parts.SequenceEqual(other._parts);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as MultiModalContent);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in _parts)
            hash.Add(part);
        return hash.ToHashCode();
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(MultiModalContent? left, MultiModalContent? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(MultiModalContent? left, MultiModalContent? right) =>
        !(left == right);
}
