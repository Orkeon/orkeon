using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// Dataset parsing: the REAL versioned golden dataset, the two accepted YAML
/// shapes, and the loud validation failures.
/// </summary>
public sealed class RagEvalDatasetYamlLoaderTests
{
    // --- the real golden.yaml ---

    [Fact]
    public void Parse_TheRealGoldenDataset_YieldsTheDocumentedShape()
    {
        var goldenPath = Path.Combine(
            FindRepositoryRoot(), "examples", "rag", "eval", "golden.yaml");
        var dataset = RagEvalDatasetYamlLoader.Parse(
            File.ReadAllText(goldenPath), "golden", goldenPath);

        Assert.Equal("golden", dataset.Name);
        Assert.Equal("./corpus", dataset.CorpusPath);
        Assert.Equal("rag-eval-golden", dataset.DefaultCollection);
        Assert.Equal(7, dataset.Cases.Count);

        // Every case is fully specified for the deterministic heuristic judge.
        Assert.All(dataset.Cases, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.False(string.IsNullOrWhiteSpace(c.Question));
            Assert.NotEmpty(c.Relevant);
            Assert.NotEmpty(c.ExpectedSubstrings);
            Assert.NotEmpty(c.Tags);
        });

        // The seeded hard-retrieval case (RAG-06 target) is present and tagged.
        var corrective = Assert.Single(dataset.Cases, c => c.Tags.Contains(RagEvalCase.CorrectiveTag));
        Assert.Equal("q-007", corrective.Id);
        Assert.Equal(["corpus/notes-power.md"], corrective.Relevant);
        Assert.Contains("eco mode", corrective.ExpectedSubstrings);

        // Every relevant ref points into the versioned corpus.
        var corpusDir = Path.Combine(FindRepositoryRoot(), "examples", "rag", "eval");
        Assert.All(dataset.Cases.SelectMany(c => c.Relevant), reference =>
            Assert.True(
                File.Exists(Path.Combine(corpusDir, reference.Replace('/', Path.DirectorySeparatorChar))),
                $"relevant ref '{reference}' does not exist in the versioned corpus"));
    }

    // --- shapes ---

    [Fact]
    public async Task Load_MappingShape_ThroughTheVfs()
    {
        var fs = new FakeFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadOnly)
            .AddFile("/workspace/ds.yaml", """
                name: mini
                corpus: ./docs
                collection: kb
                cases:
                  - id: q-1
                    question: "what?"
                    relevant: ["docs/a.md"]
                    expected_substrings: ["alpha"]
                    reference_answer: "Alpha."
                    tags: [facile]
                """);

        var dataset = await new RagEvalDatasetYamlLoader(fs)
            .LoadAsync("/workspace/ds.yaml", TestContext.Current.CancellationToken);

        Assert.Equal("mini", dataset.Name);
        Assert.Equal("./docs", dataset.CorpusPath);
        Assert.Equal("kb", dataset.DefaultCollection);
        var evalCase = Assert.Single(dataset.Cases);
        Assert.Equal("q-1", evalCase.Id);
        Assert.Equal(["docs/a.md"], evalCase.Relevant);
        Assert.Equal(["alpha"], evalCase.ExpectedSubstrings);
        Assert.Equal("Alpha.", evalCase.ReferenceAnswer);
        Assert.Equal(["facile"], evalCase.Tags);
    }

    [Fact]
    public void Parse_BareListShape_FallsBackToTheFileName_NoCorpus()
    {
        var dataset = RagEvalDatasetYamlLoader.Parse("""
            # bare list of cases (plan §9.1 shape)
            - id: q-1
              question: "what?"
              expected_substrings: ["alpha"]
            - id: q-2
              question: "why?"
            """, "bare", "bare.yaml");

        Assert.Equal("bare", dataset.Name);
        Assert.Null(dataset.CorpusPath);
        Assert.Null(dataset.DefaultCollection);
        Assert.Equal(2, dataset.Cases.Count);
        Assert.Empty(dataset.Cases[1].ExpectedSubstrings);
    }

    // --- loud failures ---

    [Fact]
    public void Parse_FailsLoudly_OnDuplicateIds_MissingQuestions_AndEmptyDatasets()
    {
        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            RagEvalDatasetYamlLoader.Parse("""
                cases:
                  - { id: q-1, question: "a?" }
                  - { id: q-1, question: "b?" }
                """, "x", "x.yaml"));
        Assert.Contains("duplicate case id 'q-1'", duplicate.Message, StringComparison.Ordinal);

        var noQuestion = Assert.Throws<InvalidOperationException>(() =>
            RagEvalDatasetYamlLoader.Parse("""
                cases:
                  - { id: q-1 }
                """, "x", "x.yaml"));
        Assert.Contains("no 'question'", noQuestion.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            RagEvalDatasetYamlLoader.Parse("cases: []", "x", "x.yaml"));

        Assert.Throws<InvalidOperationException>(() =>
            RagEvalDatasetYamlLoader.Parse("not: [valid", "x", "x.yaml"));
    }

    [Fact]
    public async Task Load_MissingFile_ThrowsFileNotFound()
    {
        var fs = new FakeFileSystemService().AddMount("/workspace", FileAccessRights.ReadOnly);

        await Assert.ThrowsAsync<FileNotFoundException>(() => new RagEvalDatasetYamlLoader(fs)
            .LoadAsync("/workspace/absent.yaml", TestContext.Current.CancellationToken));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Orkeon.sln not found above the test base directory.");
    }
}
