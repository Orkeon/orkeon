using Microsoft.ML.Tokenizers;

namespace Orkeon.Rag.Onnx.Reranking;

/// <summary>
/// Encodes (query, passage) pairs into BERT cross-encoder input tensors:
/// <c>[CLS] query [SEP] passage [SEP]</c> with token-type ids (0 for the query
/// segment, 1 for the passage segment), padded to the longest sequence of the
/// batch. Pure tokenization + shaping — no ONNX inference, hence unit-testable
/// without the native runtime.
/// </summary>
public sealed class CrossEncoderBatchEncoder
{
    private readonly BertTokenizer _tokenizer;
    private readonly int _maxSequenceLength;

    /// <summary>Creates the encoder.</summary>
    /// <param name="tokenizer">BERT WordPiece tokenizer built from the model's vocab.</param>
    /// <param name="maxSequenceLength">Maximum encoded length, special tokens included (512 for ms-marco-MiniLM-L-6-v2).</param>
    public CrossEncoderBatchEncoder(BertTokenizer tokenizer, int maxSequenceLength = 512)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSequenceLength, 8);

        _tokenizer = tokenizer;
        _maxSequenceLength = maxSequenceLength;
    }

    /// <summary>
    /// Encodes one query against a batch of passages. Sequences longer than the
    /// maximum length are truncated passage-first (the query keeps at least half
    /// of the token budget only when the passage needs the rest).
    /// </summary>
    public CrossEncoderBatch Encode(string query, IReadOnlyList<string> passages)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(passages);
        if (passages.Count == 0)
        {
            throw new ArgumentException("At least one passage is required.", nameof(passages));
        }

        var queryIds = _tokenizer.EncodeToIds(
            query, addSpecialTokens: false, considerPreTokenization: true, considerNormalization: true);

        var rows = new (IReadOnlyList<int> InputIds, IReadOnlyList<int> TypeIds)[passages.Count];
        var maxLength = 0;

        for (var i = 0; i < passages.Count; i++)
        {
            var passageIds = _tokenizer.EncodeToIds(
                passages[i] ?? string.Empty,
                addSpecialTokens: false, considerPreTokenization: true, considerNormalization: true);

            var (querySegment, passageSegment) = Truncate(queryIds, passageIds);

            var inputIds = _tokenizer.BuildInputsWithSpecialTokens(querySegment, passageSegment);
            var typeIds = _tokenizer.CreateTokenTypeIdsFromSequences(querySegment, passageSegment);

            rows[i] = (inputIds, typeIds);
            maxLength = Math.Max(maxLength, inputIds.Count);
        }

        var count = passages.Count;
        var flatInputIds = new long[count * maxLength];
        var flatAttention = new long[count * maxLength];
        var flatTypeIds = new long[count * maxLength];
        long padId = _tokenizer.PaddingTokenId;

        for (var i = 0; i < count; i++)
        {
            var (inputIds, typeIds) = rows[i];
            var offset = i * maxLength;
            for (var j = 0; j < maxLength; j++)
            {
                if (j < inputIds.Count)
                {
                    flatInputIds[offset + j] = inputIds[j];
                    flatAttention[offset + j] = 1;
                    flatTypeIds[offset + j] = typeIds[j];
                }
                else
                {
                    flatInputIds[offset + j] = padId;
                    // Attention mask and token-type ids stay 0 on padding.
                }
            }
        }

        return new CrossEncoderBatch
        {
            InputIds = flatInputIds,
            AttentionMask = flatAttention,
            TokenTypeIds = flatTypeIds,
            Count = count,
            SequenceLength = maxLength,
        };
    }

    private (IReadOnlyList<int> Query, IReadOnlyList<int> Passage) Truncate(
        IReadOnlyList<int> queryIds,
        IReadOnlyList<int> passageIds)
    {
        // 3 special tokens: [CLS] … [SEP] … [SEP].
        var budget = _maxSequenceLength - 3;
        var queryKeep = queryIds.Count;
        var passageKeep = passageIds.Count;

        if (queryKeep + passageKeep > budget)
        {
            queryKeep = Math.Min(queryKeep, Math.Max(budget / 2, budget - passageKeep));
            passageKeep = Math.Min(passageKeep, budget - queryKeep);
        }

        return (
            queryKeep == queryIds.Count ? queryIds : [.. queryIds.Take(queryKeep)],
            passageKeep == passageIds.Count ? passageIds : [.. passageIds.Take(passageKeep)]);
    }
}

/// <summary>
/// Row-major encoded batch: flattened <c>[Count, SequenceLength]</c> tensors
/// ready for the ONNX <c>input_ids</c> / <c>attention_mask</c> /
/// <c>token_type_ids</c> inputs.
/// </summary>
public sealed record CrossEncoderBatch
{
    /// <summary>Flattened token ids, length <c>Count * SequenceLength</c>.</summary>
    public required ReadOnlyMemory<long> InputIds { get; init; }

    /// <summary>Flattened attention mask (1 = real token, 0 = padding).</summary>
    public required ReadOnlyMemory<long> AttentionMask { get; init; }

    /// <summary>Flattened segment ids (0 = query, 1 = passage, 0 on padding).</summary>
    public required ReadOnlyMemory<long> TokenTypeIds { get; init; }

    /// <summary>Number of (query, passage) rows.</summary>
    public required int Count { get; init; }

    /// <summary>Padded sequence length of every row.</summary>
    public required int SequenceLength { get; init; }
}
