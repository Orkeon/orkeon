using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>One image of the collection, and everything known about how it was taken.</summary>
internal sealed record CaptureManifestImage
{
    /// <summary>Path relative to the campaign's directory.</summary>
    [JsonPropertyName("file")]
    public required string File { get; init; }

    /// <summary>The stop's slug.</summary>
    [JsonPropertyName("stop")]
    public required string Stop { get; init; }

    /// <summary>Its grouping.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>The screen it stands on.</summary>
    [JsonPropertyName("screen")]
    public required string Screen { get; init; }

    /// <summary>Which seeded machine it stands on.</summary>
    [JsonPropertyName("world")]
    public required string World { get; init; }

    /// <summary>Language, theme and mode of the pass.</summary>
    [JsonPropertyName("appearance")]
    public required string Appearance { get; init; }

    /// <summary>Why the shot exists — the stop's own words.</summary>
    [JsonPropertyName("because")]
    public required string Because { get; init; }

    /// <summary>The gates the stop claims to light up.</summary>
    [JsonPropertyName("covers")]
    public IReadOnlyList<string> Covers { get; init; } = [];

    /// <summary>
    /// Digest of the PNG. Re-run the campaign after a UI change and diff two manifests by stop:
    /// what comes back is the exact list of screens that moved — which is the whole point of
    /// keeping a fidelity reference, and is impossible without this field.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary><c>written</c> or <c>failed</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Why it failed, when it did.</summary>
    [JsonPropertyName("problems")]
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>Binding errors WPF traced while this shot was being arranged.</summary>
    [JsonPropertyName("binding_errors")]
    public IReadOnlyList<string> BindingErrors { get; init; } = [];
}

/// <summary>
/// The index that makes a collection of several hundred images navigable — and countable.
/// <para>
/// Without it a failed stop is invisible: the directory just has one fewer file than anybody
/// remembers. With it, every image carries why it exists and every hole carries why it is a hole.
/// </para>
/// </summary>
internal sealed record CaptureManifest
{
    /// <summary>Shape version of this document.</summary>
    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    /// <summary>Studio's own version.</summary>
    [JsonPropertyName("studio_version")]
    public required string StudioVersion { get; init; }

    /// <summary>Window size, in device-independent units.</summary>
    [JsonPropertyName("window")]
    public required string Window { get; init; }

    /// <summary>Render scale the PNGs were taken at.</summary>
    [JsonPropertyName("scale")]
    public double Scale { get; init; } = 1;

    /// <summary>The machine's own DPI scale, so a shot from a scaled display is identifiable.</summary>
    [JsonPropertyName("machine_dpi_scale")]
    public double MachineDpiScale { get; init; } = 1;

    /// <summary>How many shots were planned.</summary>
    [JsonPropertyName("planned")]
    public int Planned { get; init; }

    /// <summary>How many files were actually written.</summary>
    [JsonPropertyName("written")]
    public int Written { get; init; }

    /// <summary>How many stops failed.</summary>
    [JsonPropertyName("failed")]
    public int Failed { get; init; }

    /// <summary>Properties of the collection a reviewer should know before chasing a difference.</summary>
    [JsonPropertyName("notes")]
    public IReadOnlyList<string> Notes { get; init; } =
    [
        "RenderTargetBitmap draws text with grayscale antialiasing rather than ClearType. Glyph "
        + "metrics match the running application exactly; the antialiasing does not.",
        "Spinners are posed at a fixed angle and the assistant's busy signals at fixed phases, so "
        + "two runs of the campaign differ only where the interface differs.",
    ];

    /// <summary>Every image, in the order it was taken.</summary>
    [JsonPropertyName("images")]
    public IReadOnlyList<CaptureManifestImage> Images { get; init; } = [];

    /// <summary>The document, ready to write.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
