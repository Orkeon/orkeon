using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// URL value object with validation and parsing.
/// </summary>
public sealed record Url : ValueObjectRecord
{
    /// <summary>Gets the URL string value.</summary>
    public string Value { get; }
    /// <summary>Gets the parsed <see cref="System.Uri"/> for the URL.</summary>
    public Uri Uri { get; }

    /// <summary>Initializes a new <see cref="Url"/> with validation.</summary>
    /// <param name="value">The raw URL string (must be http or https).</param>
    private Url(string value)
    {
        EnsureNotNullOrWhiteSpace(value, nameof(value));

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URL format", nameof(value));

        // Ensure it's a valid HTTP or HTTPS URL
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException("Invalid URL format", nameof(value));

        Value = value;
        Uri = uri;
        Validate();
    }

    /// <summary>Gets the URL scheme (http or https).</summary>
    public string Scheme => Uri.Scheme;
    /// <summary>Gets the host name.</summary>
    public string Host => Uri.Host;
    /// <summary>Gets the port number.</summary>
    public int Port => Uri.Port;
    /// <summary>Gets the absolute path portion of the URL.</summary>
    public string Path => Uri.AbsolutePath;

    /// <summary>Gets whether the URL uses HTTPS.</summary>
    public bool IsHttps => Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    /// <summary>Gets whether the URL is secure (HTTPS).</summary>
    public bool IsSecure => IsHttps;

    /// <summary>Creates a <see cref="Url"/> from a string value.</summary>
    /// <param name="value">The raw URL string.</param>
    /// <returns>A validated <see cref="Url"/>.</returns>
    public static Url From(string value) => new(value);
    /// <summary>Implicitly converts a <see cref="Url"/> to a string.</summary>
    /// <param name="url">The URL to convert.</param>
    public static implicit operator string(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Value;
    }
    /// <summary>Implicitly converts a <see cref="Url"/> to a <see cref="System.Uri"/>.</summary>
    /// <param name="url">The URL to convert.</param>
    public static implicit operator Uri(Url url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.Uri;
    }
    /// <summary>Friendly-named alternate for the implicit conversion to <see cref="System.Uri"/>.</summary>
    /// <returns>The parsed <see cref="System.Uri"/>.</returns>
    public Uri ToUri() => Uri;
    /// <inheritdoc />
    public override string ToString() => Value;
}
