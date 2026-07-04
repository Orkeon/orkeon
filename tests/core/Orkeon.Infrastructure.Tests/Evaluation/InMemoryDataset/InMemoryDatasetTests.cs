using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class InMemoryDatasetTests
{
    private readonly InMemoryDatasetTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnPreBuiltInputs_WhenLoading()
    {
        var inputs = new List<EvaluationInput>
        {
            new("output1", "expected1", "task1"),
            new("output2", "expected2", "task2")
        };

        var dataset = InMemoryDatasetTestsFixture.CreateDataset("TestData", inputs);
        var loaded = await InMemoryDatasetTestsFixture.LoadAsync(dataset);

        Assert.Equal("TestData", dataset.Name);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("output1", loaded[0].Output);
        Assert.Equal("output2", loaded[1].Output);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenInputListIsEmpty()
    {
        var dataset = InMemoryDatasetTestsFixture.CreateDataset("Empty", Array.Empty<EvaluationInput>());
        var loaded = await InMemoryDatasetTestsFixture.LoadAsync(dataset);

        Assert.Empty(loaded);
    }

    [Fact]
    public void ShouldSetName_WhenConstructing()
    {
        var dataset = InMemoryDatasetTestsFixture.CreateDataset("MyDataset", Array.Empty<EvaluationInput>());
        Assert.Equal("MyDataset", dataset.Name);
    }

    [Fact]
    public void ShouldThrow_WhenNameIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InMemoryDatasetTestsFixture.CreateDataset(null!, Array.Empty<EvaluationInput>()));
    }

    [Fact]
    public void ShouldThrow_WhenInputsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InMemoryDatasetTestsFixture.CreateDataset("Test", null!));
    }
}
