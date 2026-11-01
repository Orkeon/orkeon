using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Tests.Constants.Agent;

public class AgentDefaultsTests
{
    [Trait("BR", "#283")]
    [Theory]
    [InlineData("dependency_graph")]
    [InlineData("sub_graph")]
    [InlineData("edge_list")]
    [InlineData("flow_trace")]
    [InlineData("impact_analysis")]
    [InlineData("codebase_map")]
    [InlineData("package_summary")]
    [InlineData("symbol_detail")]
    [InlineData("codebase_search")]
    [InlineData("structural_search")]
    [InlineData("structural_rewrite")]
    [InlineData("affected_tests")]
    public void ShouldResolveTheAnalysisBound_WhenTheToolAnswersWithAGraphOrAPage(string toolName)
    {
        Assert.Equal(AgentDefaults.AnalysisToolResultLength, AgentDefaults.ResolveMaxToolResultLength(toolName));
        Assert.Equal(12_000, AgentDefaults.ResolveMaxToolResultLength(toolName.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("file_read", 32_000)]
    [InlineData("node_read", 256_000)]
    [InlineData("shell_command", 10_000)]
    public void ShouldResolveTheWiderBound_WhenTheToolIsListed(string toolName, int expected)
    {
        Assert.Equal(expected, AgentDefaults.ResolveMaxToolResultLength(toolName));
    }

    [Theory]
    [InlineData("statement_query")]
    [InlineData("grep")]
    [InlineData("")]
    public void ShouldResolveTheDefaultBound_WhenTheToolIsNotListed(string toolName)
    {
        Assert.Equal(AgentDefaults.MaxToolResultLength, AgentDefaults.ResolveMaxToolResultLength(toolName));
    }
}
