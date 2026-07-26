using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Onnx.Model;
using Orkeon.Rag.Onnx.Reranking;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Onnx.Tests;

/// <summary>
/// Model-source resolution tests: embedded package probing and the actionable
/// error paths. No ONNX inference (the sessions are never initialized on the
/// error paths).
/// </summary>
public class OnnxModelResolutionTests
{
    private const string AbsentAssembly = "Orkeon.Rag.Onnx.Model.DoesNotExist";

    private static ScoredChunk[] OneCandidate() =>
    [
        new ScoredChunk
        {
            Chunk = new Chunk
            {
                Id = "chunk-0",
                DocumentId = "doc",
                SourceId = "source",
                Content = "some content",
            },
            Score = 0.5,
        },
    ];

    [Fact]
    public void TryLoadEmbedded_ModelPackageReferenced_YieldsWeightsAndVocab()
    {
        var found = OnnxRerankerModelSource.TryLoadEmbedded(
            MsMarcoMiniLmModel.AssemblyName, out var modelBytes, out var vocabStream);

        Assert.True(found);
        Assert.NotNull(modelBytes);
        Assert.True(modelBytes!.Length > 1_000_000, "embedded ONNX weights should be megabytes large");
        using (vocabStream)
        {
            Assert.NotNull(vocabStream);
            using var reader = new StreamReader(vocabStream!);
            Assert.Equal("[PAD]", reader.ReadLine());
        }
    }

    [Fact]
    public void TryLoadEmbedded_AssemblyAbsent_ReturnsFalse()
    {
        var found = OnnxRerankerModelSource.TryLoadEmbedded(
            AbsentAssembly, out var modelBytes, out var vocabStream);

        using (vocabStream)
        {
            Assert.False(found);
            Assert.Null(modelBytes);
            Assert.Null(vocabStream);
        }
    }

    [Fact]
    public async Task Rerank_NoModelPackage_NoModelPath_FailsWithActionableMessage()
    {
        using var reranker = new OnnxCrossEncoderReranker(
            new ThrowingFileSystemService(), options: null, logger: null,
            modelAssemblyName: AbsentAssembly);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reranker.RerankAsync("query", OneCandidate(), topN: 5, TestContext.Current.CancellationToken));

        Assert.Contains("Orkeon.Rag.Onnx.Model", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ModelPath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("never downloads", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rerank_ModelPathWithoutVocabPath_FailsLoudly()
    {
        using var reranker = new OnnxCrossEncoderReranker(
            new ThrowingFileSystemService(),
            options: new OnnxRerankerOptions { ModelPath = "/models/model.onnx" },
            logger: null,
            modelAssemblyName: AbsentAssembly);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reranker.RerankAsync("query", OneCandidate(), topN: 5, TestContext.Current.CancellationToken));

        Assert.Contains("VocabPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rerank_ModelPathMissingOnVfs_FailsWithActionableMessage()
    {
        var fileSystem = new FakeFileSystemService();

        using var reranker = new OnnxCrossEncoderReranker(
            fileSystem,
            options: new OnnxRerankerOptions
            {
                ModelPath = "/models/model.onnx",
                VocabPath = "/models/vocab.txt",
            },
            logger: null,
            modelAssemblyName: AbsentAssembly);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reranker.RerankAsync("query", OneCandidate(), topN: 5, TestContext.Current.CancellationToken));

        Assert.Contains("/models/model.onnx", exception.Message, StringComparison.Ordinal);
        Assert.Contains("download-reranker-model.sh", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rerank_EmptyCandidates_ShortCircuitsWithoutModelResolution()
    {
        // Even with no model source at all, an empty batch never touches the model.
        using var reranker = new OnnxCrossEncoderReranker(
            new ThrowingFileSystemService(), options: null, logger: null,
            modelAssemblyName: AbsentAssembly);

        Assert.Empty(await reranker.RerankAsync("query", [], topN: 5, TestContext.Current.CancellationToken));
        Assert.Empty(await reranker.RerankAsync("query", OneCandidate(), topN: 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EmbeddedResourceNames_MatchTheModelPackageContract()
    {
        Assert.Equal(MsMarcoMiniLmModel.AssemblyName, OnnxRerankerModelSource.ModelAssemblyName);
        Assert.Equal(MsMarcoMiniLmModel.ModelResourceName, OnnxRerankerModelSource.ModelResourceName);
        Assert.Equal(MsMarcoMiniLmModel.VocabResourceName, OnnxRerankerModelSource.VocabResourceName);
    }
}
