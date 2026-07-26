namespace Orkeon.Rag.Onnx.Reranking;

/// <summary>
/// Options of the ONNX cross-encoder reranker. All model-file settings are
/// <b>VFS virtual paths</b> (read through <c>IFileSystemService</c>) and are only
/// consulted when the companion package <c>Orkeon.Rag.Onnx.Model</c> (embedded
/// weights, guaranteed offline) is not referenced.
/// </summary>
public sealed record OnnxRerankerOptions
{
    /// <summary>
    /// VFS virtual path to the cross-encoder ONNX weights (e.g.
    /// <c>/models/msmarco-minilm-l6-v2.quant.onnx</c>). Ignored when the
    /// <c>Orkeon.Rag.Onnx.Model</c> package is referenced. When set,
    /// <see cref="VocabPath"/> must be set too. The runtime never downloads
    /// weights — see <c>tools/download-reranker-model.sh</c>.
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// VFS virtual path to the matching BERT WordPiece <c>vocab.txt</c>.
    /// Required when <see cref="ModelPath"/> is set.
    /// </summary>
    public string? VocabPath { get; init; }

    /// <summary>Number of (query, passage) pairs scored per ONNX inference call.</summary>
    public int BatchSize { get; init; } = 16;

    /// <summary>
    /// Maximum token length of an encoded (query, passage) pair, special tokens
    /// included. ms-marco-MiniLM-L-6-v2 supports up to 512.
    /// </summary>
    public int MaxSequenceLength { get; init; } = 512;
}
