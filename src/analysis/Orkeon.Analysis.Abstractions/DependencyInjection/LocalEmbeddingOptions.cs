namespace Orkeon.Analysis.Abstractions.DependencyInjection;

/// <summary>
/// Options for the on-device local embedding provider (implemented in the opt-in
/// package <c>Orkeon.Tools.Embeddings.Local</c>).
/// All fields are optional — defaults yield a working zero-config setup.
/// </summary>
public sealed record LocalEmbeddingOptions
{
    /// <summary>
    /// Optional <b>virtual</b> path to a custom .onnx model file (e.g. <c>"/models/my-custom.onnx"</c>).
    /// When <see langword="null"/>, the embedded BGE-micro-v2 model shipped by
    /// <c>SmartComponents.LocalEmbeddings</c> is used (384 dimensions).
    /// <para>
    /// The path is resolved by <c>IFileSystemService</c> against the configured mounts
    /// (mandatory VFS routing — see <c>docs/architecture/vfs-compliance.md</c>). Physical
    /// disk paths (e.g. <c>"C:/models/foo.onnx"</c>) are <b>not</b> accepted: declare a
    /// mount <c>"/models:r=C:/path/to/models"</c> in <c>FileSystem.Mounts</c> instead.
    /// </para>
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// Maximum degree of parallelism used when embedding a batch of texts.
    /// Defaults to <see cref="Environment.ProcessorCount"/>. Setting to 1 forces
    /// sequential embedding (useful on constrained hosts or for deterministic tests).
    /// </summary>
    public int MaxConcurrency { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Per-text character cap applied before embedding. Long inputs are truncated to
    /// this length to bound CPU usage. <see langword="null"/> disables truncation.
    /// Mirrors <c>EmbeddingOptions.MaxTextChars</c> semantics in <c>RaggableTreeOptions</c>.
    /// Recommended: 2000 (BGE-micro-v2 has a 512-token context window).
    /// </summary>
    public int? MaxTextChars { get; init; } = 2000;
}
