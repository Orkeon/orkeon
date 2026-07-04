using Orkeon.Domain.SharedKernel;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class JsonFileDatasetTests
{
    private static FakeFileSystemService BuildFs(string virtualPath, string json)
    {
        var fs = new FakeFileSystemService();
        fs.AddFile(virtualPath, json);
        return fs;
    }

    [Fact]
    public async Task ShouldLoadInputs_WhenJsonFileIsValid()
    {
        const string path = "/datasets/dataset.json";
        var json = """
            [
                {
                    "output": "AI is transforming technology.",
                    "expected_output": "AI is changing the world.",
                    "task_description": "Summarize AI impact",
                    "context": "Recent AI advances"
                },
                {
                    "output": "42",
                    "expected_output": "42",
                    "task_description": "Compute answer",
                    "expected_format": "Text"
                }
            ]
            """;
        var fs = BuildFs(path, json);
        var dataset = JsonFileDatasetTestsFixture.CreateDataset("TestDataset", path, fs);

        var inputs = await dataset.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("TestDataset", dataset.Name);
        Assert.Equal(2, inputs.Count);
        Assert.Equal("AI is transforming technology.", inputs[0].Output);
        Assert.Equal("AI is changing the world.", inputs[0].ExpectedOutput);
        Assert.Equal("Summarize AI impact", inputs[0].TaskDescription);
        Assert.Equal("Recent AI advances", inputs[0].Context);
        Assert.Equal("42", inputs[1].Output);
        Assert.Equal(OutputFormat.Text, inputs[1].ExpectedFormat);
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenFileIsMissing()
    {
        const string path = "/datasets/missing.json";
        var fs = new FakeFileSystemService();
        var dataset = JsonFileDatasetTestsFixture.CreateDataset("Missing", path, fs);

        await Assert.ThrowsAsync<FileNotFoundException>(() => dataset.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldLoadCorrectly_WhenFileContainsMetadata()
    {
        const string path = "/datasets/metadata.json";
        var json = """
            [
                {
                    "output": "test output",
                    "metadata": {"expected_tools": "FileRead,WebScrape", "priority": "high"}
                }
            ]
            """;
        var fs = BuildFs(path, json);
        var dataset = JsonFileDatasetTestsFixture.CreateDataset("MetaData", path, fs);

        var inputs = await dataset.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Single(inputs);
        Assert.NotNull(inputs[0].Metadata);
        Assert.Equal("FileRead,WebScrape", inputs[0].Metadata!["expected_tools"]);
    }

    [Fact]
    public async Task ShouldReturnEmptyList_WhenJsonArrayIsEmpty()
    {
        const string path = "/datasets/empty.json";
        var fs = BuildFs(path, "[]");
        var dataset = JsonFileDatasetTestsFixture.CreateDataset("Empty", path, fs);

        var inputs = await dataset.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(inputs);
    }
}
