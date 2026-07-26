using Microsoft.ML.Tokenizers;
using Orkeon.Rag.Onnx.Reranking;
using Orkeon.Rag.Onnx.Model;

namespace Orkeon.Rag.Onnx.Tests;

/// <summary>
/// Tokenization and tensor-shaping tests — pure managed code, no ONNX
/// inference, safe in any sandbox.
/// </summary>
public class CrossEncoderBatchEncoderTests
{
    private static readonly BertTokenizer s_tokenizer = CreateTokenizer();

    private static BertTokenizer CreateTokenizer()
    {
        using var vocab = MsMarcoMiniLmModel.OpenVocabStream();
        return BertTokenizer.Create(vocab);
    }

    [Fact]
    public void Encode_SingleGoodPair_HasBertPairLayout()
    {
        var encoder = new CrossEncoderBatchEncoder(s_tokenizer);

        var batch = encoder.Encode("what is rust?", ["Rust is a systems programming language."]);

        Assert.Equal(1, batch.Count);
        Assert.True(batch.SequenceLength > 4);
        Assert.Equal(batch.Count * batch.SequenceLength, batch.InputIds.Length);
        Assert.Equal(batch.InputIds.Length, batch.AttentionMask.Length);
        Assert.Equal(batch.InputIds.Length, batch.TokenTypeIds.Length);

        var inputIds = batch.InputIds.ToArray();
        var typeIds = batch.TokenTypeIds.ToArray();

        // [CLS] query [SEP] passage [SEP]
        Assert.Equal(s_tokenizer.ClassificationTokenId, inputIds[0]);
        Assert.Equal(s_tokenizer.SeparatorTokenId, inputIds[batch.SequenceLength - 1]);
        Assert.Equal(2, inputIds.Count(id => id == s_tokenizer.SeparatorTokenId));

        // Segment ids: query segment (incl. [CLS] and first [SEP]) = 0, passage segment = 1.
        Assert.Equal(0, typeIds[0]);
        Assert.Equal(1, typeIds[batch.SequenceLength - 1]);

        // No padding on a single row: full attention.
        Assert.All(batch.AttentionMask.ToArray(), mask => Assert.Equal(1, mask));
    }

    [Fact]
    public void Encode_UnevenPassages_PadsToLongestRow()
    {
        var encoder = new CrossEncoderBatchEncoder(s_tokenizer);

        var batch = encoder.Encode("query", ["short", "a much longer passage with many more tokens than the first one"]);

        Assert.Equal(2, batch.Count);

        var inputIds = batch.InputIds.ToArray();
        var attention = batch.AttentionMask.ToArray();
        var typeIds = batch.TokenTypeIds.ToArray();

        // Row 0 is shorter: its tail is padding (attention 0, pad token id, type id 0).
        var row0End = batch.SequenceLength - 1;
        Assert.Equal(0, attention[row0End]);
        Assert.Equal(s_tokenizer.PaddingTokenId, inputIds[row0End]);
        Assert.Equal(0, typeIds[row0End]);

        // Row 1 is the longest: no padding at its end.
        Assert.Equal(1, attention[(2 * batch.SequenceLength) - 1]);

        // Padding never carries attention.
        for (var j = 0; j < batch.SequenceLength; j++)
        {
            if (attention[j] == 0)
            {
                Assert.Equal(s_tokenizer.PaddingTokenId, inputIds[j]);
            }
        }
    }

    [Fact]
    public void Encode_OverlongPassage_IsTruncatedToMaxSequenceLength()
    {
        const int maxLength = 32;
        var encoder = new CrossEncoderBatchEncoder(s_tokenizer, maxLength);
        var longPassage = string.Join(' ', Enumerable.Repeat("token", 500));

        var batch = encoder.Encode("what is the answer?", [longPassage]);

        Assert.Equal(maxLength, batch.SequenceLength);
        Assert.Equal(s_tokenizer.ClassificationTokenId, batch.InputIds.Span[0]);
        Assert.Equal(s_tokenizer.SeparatorTokenId, batch.InputIds.Span[maxLength - 1]);
    }

    [Fact]
    public void Encode_EmptyPassages_Throws()
    {
        var encoder = new CrossEncoderBatchEncoder(s_tokenizer);
        Assert.Throws<ArgumentException>(() => encoder.Encode("query", []));
    }

    [Fact]
    public void EmbeddedVocab_IsTheBertUncasedVocabulary()
    {
        // [PAD]/[CLS]/[SEP] must sit at the canonical BERT-uncased ids.
        Assert.Equal(0, s_tokenizer.PaddingTokenId);
        Assert.Equal(101, s_tokenizer.ClassificationTokenId);
        Assert.Equal(102, s_tokenizer.SeparatorTokenId);
    }
}
