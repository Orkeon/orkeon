using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Onnx.Reranking;

/// <summary>
/// Cross-encoder reranker over ms-marco-MiniLM-L-6-v2 (ONNX, Apache-2.0):
/// each (query, chunk) pair is scored jointly by the model — the quality stage
/// of the 50 → 5 cascade (guide §7). Deterministic, offline, zero token cost.
/// </summary>
/// <remarks>
/// <para>Model resolution order (first hit wins), performed lazily at first use:</para>
/// <list type="number">
///   <item><description>Companion package <c>Orkeon.Rag.Onnx.Model</c> referenced
///   → embedded weights (guaranteed offline, the CI path).</description></item>
///   <item><description><see cref="OnnxRerankerOptions.ModelPath"/> /
///   <see cref="OnnxRerankerOptions.VocabPath"/> — VFS virtual paths read through
///   <see cref="IFileSystemService"/>.</description></item>
///   <item><description>Otherwise: loud, actionable error. The runtime never
///   downloads weights (supply-chain decision) — see
///   <c>tools/download-reranker-model.sh</c>.</description></item>
/// </list>
/// <para>Scores are sigmoid-mapped logits in (0, 1) with
/// <see cref="ScoredChunk.ScoreOrigin"/> = <c>cross-encoder</c>.</para>
/// </remarks>
public sealed partial class OnnxCrossEncoderReranker : IReranker, IDisposable
{
    /// <summary>Canonical factory name (<c>cross-encoder</c> is the registered alias).</summary>
    public const string RerankerName = "onnx";

    /// <summary>Value stamped on <see cref="ScoredChunk.ScoreOrigin"/>.</summary>
    public const string ScoreOrigin = "cross-encoder";

    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string TokenTypeIdsName = "token_type_ids";

    private readonly OnnxRerankerOptions _options;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<OnnxCrossEncoderReranker> _logger;
    private readonly string _modelAssemblyName;
    private readonly object _initGate = new();

    private Task? _initTask;
    private InferenceSession? _session;
    private CrossEncoderBatchEncoder? _encoder;
    private bool _disposed;

    /// <summary>Creates the reranker; the model is resolved lazily at first use.</summary>
    /// <param name="fileSystem">VFS service used to read <see cref="OnnxRerankerOptions.ModelPath"/> / <see cref="OnnxRerankerOptions.VocabPath"/> when configured.</param>
    /// <param name="options">Options; <c>null</c> selects the defaults (embedded weights only).</param>
    /// <param name="logger">Optional logger.</param>
    public OnnxCrossEncoderReranker(
        IFileSystemService fileSystem,
        OnnxRerankerOptions? options = null,
        ILogger<OnnxCrossEncoderReranker>? logger = null)
        : this(fileSystem, options, logger, OnnxRerankerModelSource.ModelAssemblyName)
    {
    }

    // Test seam: lets the unit suite simulate an absent Orkeon.Rag.Onnx.Model package.
    internal OnnxCrossEncoderReranker(
        IFileSystemService fileSystem,
        OnnxRerankerOptions? options,
        ILogger<OnnxCrossEncoderReranker>? logger,
        string modelAssemblyName)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        _options = options ?? new OnnxRerankerOptions();
        _logger = logger ?? NullLogger<OnnxCrossEncoderReranker>.Instance;
        _modelAssemblyName = modelAssemblyName;
    }

    /// <inheritdoc />
    public string Name => RerankerName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (candidates.Count == 0 || topN <= 0)
        {
            return [];
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var scores = new double[candidates.Count];
        var batchSize = Math.Max(1, _options.BatchSize);

        for (var start = 0; start < candidates.Count; start += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var length = Math.Min(batchSize, candidates.Count - start);
            var passages = new string[length];
            for (var i = 0; i < length; i++)
            {
                passages[i] = candidates[start + i].Chunk.Content;
            }

            var batchScores = ScoreBatch(query, passages);
            Array.Copy(batchScores, 0, scores, start, length);
        }

        return [.. candidates
            .Select((candidate, index) => candidate with
            {
                Score = scores[index],
                ScoreOrigin = ScoreOrigin,
            })
            .OrderByDescending(scored => scored.Score)
            .Take(topN)];
    }

    private double[] ScoreBatch(string query, IReadOnlyList<string> passages)
    {
        var batch = _encoder!.Encode(query, passages);
        var shape = new[] { batch.Count, batch.SequenceLength };

        var inputs = new List<NamedOnnxValue>(3)
        {
            NamedOnnxValue.CreateFromTensor(
                InputIdsName, new DenseTensor<long>(MemoryMarshal.AsMemory(batch.InputIds), shape)),
            NamedOnnxValue.CreateFromTensor(
                AttentionMaskName, new DenseTensor<long>(MemoryMarshal.AsMemory(batch.AttentionMask), shape)),
        };

        // Some ONNX exports fold the segment embedding away — only feed what the model declares.
        if (_session!.InputMetadata.ContainsKey(TokenTypeIdsName))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(
                TokenTypeIdsName, new DenseTensor<long>(MemoryMarshal.AsMemory(batch.TokenTypeIds), shape)));
        }

        using var results = _session.Run(inputs);
        var logits = results[0].AsTensor<float>();

        var scores = new double[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            scores[i] = Sigmoid(logits[i, 0]);
        }

        return scores;
    }

    private static double Sigmoid(double logit) => 1.0 / (1.0 + Math.Exp(-logit));

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        Task initTask;
        lock (_initGate)
        {
            _initTask ??= InitializeCoreAsync();
            initTask = _initTask;
        }

        return initTask.WaitAsync(cancellationToken);
    }

    private async Task InitializeCoreAsync()
    {
        var (modelBytes, vocabStream, source) = await ResolveModelAsync().ConfigureAwait(false);

        BertTokenizer tokenizer;
        using (vocabStream)
        {
            tokenizer = await BertTokenizer.CreateAsync(vocabStream).ConfigureAwait(false);
        }

        _encoder = new CrossEncoderBatchEncoder(tokenizer, _options.MaxSequenceLength);
        _session = new InferenceSession(modelBytes);
        LogModelLoaded(source, modelBytes.Length);
    }

    private async Task<(byte[] ModelBytes, Stream VocabStream, string Source)> ResolveModelAsync()
    {
        // ① Embedded weights from the companion package (offline guarantee).
        if (OnnxRerankerModelSource.TryLoadEmbedded(_modelAssemblyName, out var embeddedModel, out var embeddedVocab))
        {
            return (embeddedModel!, embeddedVocab!, "embedded (Orkeon.Rag.Onnx.Model)");
        }

        // ② Configured VFS paths.
        if (!string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            if (string.IsNullOrWhiteSpace(_options.VocabPath))
            {
                throw new InvalidOperationException(
                    "OnnxRerankerOptions.ModelPath is set but VocabPath is not — the cross-encoder needs " +
                    "the matching BERT vocab.txt. Set OnnxRerankerOptions.VocabPath to its VFS path.");
            }

            var modelBytes = await _fileSystem.TryReadAllBytesAsync(_options.ModelPath, CancellationToken.None)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Reranker model not found at VFS path '{_options.ModelPath}'. Provision it with " +
                    "tools/download-reranker-model.sh (SHA-256 verified) or reference the Orkeon.Rag.Onnx.Model package.");

            var vocabBytes = await _fileSystem.TryReadAllBytesAsync(_options.VocabPath!, CancellationToken.None)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Reranker vocab not found at VFS path '{_options.VocabPath}'. Provision it with " +
                    "tools/download-reranker-model.sh (SHA-256 verified) or reference the Orkeon.Rag.Onnx.Model package.");

            return (modelBytes, new MemoryStream(vocabBytes), $"vfs ({_options.ModelPath})");
        }

        // ③ Nothing available: loud, actionable error — never a silent download.
        throw new InvalidOperationException(
            "No cross-encoder model available for the ONNX reranker. Either reference the " +
            "Orkeon.Rag.Onnx.Model package (embedded ms-marco-MiniLM-L-6-v2 weights, guaranteed offline) " +
            "or set OnnxRerankerOptions.ModelPath and VocabPath to VFS paths pointing at the model files " +
            "(tools/download-reranker-model.sh downloads and SHA-256-verifies them). " +
            "The runtime never downloads weights by itself.");
    }

    /// <summary>Releases the ONNX session. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session?.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cross-encoder reranker model loaded from {Source} ({ModelBytes} bytes).")]
    private partial void LogModelLoaded(string source, int modelBytes);
}
