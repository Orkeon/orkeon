using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ProcessTypeTests
{
    [Fact]
    public void ShouldHaveExpectedCount_WhenGettingAll()
    {
        Assert.Equal(6, ProcessType.All.Count);
    }

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    [InlineData("Parallel")]
    [InlineData("Graph")]
    [InlineData("Autonomous")]
    public void ShouldParse_WhenUsingFromWithValidString(string value)
    {
        var processType = ProcessType.From(value);
        Assert.Equal(value, processType.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidString()
    {
        Assert.Throws<ArgumentException>(() => ProcessType.From("Invalid"));
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingImplicitConversion()
    {
        string value = ProcessType.Sequential;
        Assert.Equal("Sequential", value);
    }

    /// <summary>
    /// GAP-22 — Sequential honours a task's asyncExecution and Parallel accepts it (a wave already runs
    /// at once); the four modes that order their tasks themselves do not accept it.
    /// </summary>
    [Theory]
    [InlineData("Sequential", true)]
    [InlineData("Parallel", true)]
    [InlineData("Hierarchical", false)]
    [InlineData("Consensual", false)]
    [InlineData("Graph", false)]
    [InlineData("Autonomous", false)]
    public void Only_sequential_and_parallel_accept_async_execution(string value, bool accepts)
    {
        Assert.Equal(accepts, ProcessType.From(value).AcceptsAsyncExecution);
    }

    /// <summary>
    /// GAP-33 — a manager agent means something in two modes: Hierarchical (it assigns each task
    /// and reviews its output) and Consensual (it arbitrates the ManagerDecision fallback). The four
    /// others have no manager agent: one named for them would be one more worker.
    /// </summary>
    [Theory]
    [InlineData("Hierarchical", true)]
    [InlineData("Consensual", true)]
    [InlineData("Sequential", false)]
    [InlineData("Parallel", false)]
    [InlineData("Graph", false)]
    [InlineData("Autonomous", false)]
    public void Only_hierarchical_and_consensual_accept_a_manager_agent(string value, bool accepts)
    {
        Assert.Equal(accepts, ProcessType.From(value).AcceptsManagerAgent);
    }

    /// <summary>
    /// GAP-33 — a manager LLM (C# <c>WithManagerLlm</c>) is read by Hierarchical (the manager assigns
    /// and reviews on it) and Autonomous (the manager hands the tasks out on it); the four others
    /// never call it.
    /// </summary>
    [Theory]
    [InlineData("Hierarchical", true)]
    [InlineData("Autonomous", true)]
    [InlineData("Sequential", false)]
    [InlineData("Parallel", false)]
    [InlineData("Graph", false)]
    [InlineData("Consensual", false)]
    public void Only_hierarchical_and_autonomous_accept_a_manager_llm(string value, bool accepts)
    {
        Assert.Equal(accepts, ProcessType.From(value).AcceptsManagerLlm);
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenCheckingIsDefault()
    {
#pragma warning disable CS1718 // Testing reflexive equality
        Assert.True(ProcessType.Sequential == ProcessType.Sequential);
#pragma warning restore CS1718
        Assert.False(ProcessType.Parallel == ProcessType.Sequential);
    }
}
