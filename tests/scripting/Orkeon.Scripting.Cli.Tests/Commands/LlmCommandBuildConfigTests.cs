using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The probe's option-to-config mapping. Each mapped field exists because a real campaign
/// failed without it (timeout: cold Ollama 2026-08-01; temperature: the flickering M2
/// verdict; workspace id: Anthropic's identity-linked keys refusing every request without
/// an <c>anthropic-workspace-id</c> header, 2026-08-30).
/// </summary>
public sealed class LlmCommandBuildConfigTests
{
    private static LlmProbeCommandOptions Options() => new()
    {
        Provider = "anthropic",
        Model = "claude-sonnet-5",
    };

    [Fact]
    public void ShouldCarryTheWorkspaceId_WhenTheOptionIsSet()
    {
        var options = Options();
        options.WorkspaceId = "wrkspc_01TestWorkspaceIdentifier";

        var config = LlmCommand.BuildConfig(options, apiKey: "k");

        Assert.Equal("wrkspc_01TestWorkspaceIdentifier", config.WorkspaceId);
    }

    [Fact]
    public void ShouldLeaveTheWorkspaceIdUnset_WhenTheOptionIsAbsent()
    {
        var config = LlmCommand.BuildConfig(Options(), apiKey: "k");

        Assert.Null(config.WorkspaceId);
    }

    /// <summary>
    /// `gpt-5.6-sol` refuses function tools on chat/completions unless reasoning is
    /// explicitly off (2026-08-30) — the same shape as the pinned temperature: the catalogue
    /// declares what the model demands, the campaign passes it through this option.
    /// </summary>
    [Fact]
    public void ShouldCarryTheThinkingEffort_WhenTheOptionIsSet()
    {
        var options = Options();
        options.ThinkingEffort = "none";

        var config = LlmCommand.BuildConfig(options, apiKey: "k");

        Assert.Equal("none", config.Thinking?.Effort);
    }

    [Fact]
    public void ShouldLeaveThinkingUnset_WhenTheOptionIsAbsent()
    {
        var config = LlmCommand.BuildConfig(Options(), apiKey: "k");

        Assert.Null(config.Thinking);
    }

    /// <summary>
    /// GAP-18: a provider with no default of its own (Azure serves the deployments an operator
    /// named) and no <c>--model</c> fell back to OpenAI's default model; the probe names none
    /// now, and the provider runs on the model it is configured with.
    /// </summary>
    [Fact]
    public void ShouldNameNoModel_WhenNeitherTheOptionNorTheProviderHasOne()
    {
        var options = new LlmProbeCommandOptions { Provider = "azure" };

        var config = LlmCommand.BuildConfig(options, apiKey: "k");

        Assert.Equal(string.Empty, config.Model);
    }

    [Theory]
    [InlineData("none, minimal ,high,,", new[] { "none", "minimal", "high" })]
    [InlineData("low", new[] { "low" })]
    public void ShouldSplitTheSweepEfforts_IntoTrimmedNonEmptyEntries(string raw, string[] expected)
    {
        Assert.Equal(expected, LlmCommand.ParseSweepEfforts(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void ShouldLeaveTheSweepUnset_WhenTheOptionIsBlank(string? raw)
    {
        Assert.Null(LlmCommand.ParseSweepEfforts(raw));
    }

    [Fact]
    public void ShouldParseTheSweepEffortsOption_FromTheCommandLine()
    {
        var parsed = CommandLine.Parser.Default.ParseArguments<LlmProbeCommandOptions>(
            ["--provider", "openai", "--sweep-efforts", "none,high"]);

        var options = Assert.IsType<CommandLine.Parsed<LlmProbeCommandOptions>>(parsed).Value;
        Assert.Equal("none,high", options.SweepEfforts);
    }

    [Fact]
    public void ShouldParseTheRawReasoningOption_FromTheCommandLine()
    {
        var parsed = CommandLine.Parser.Default.ParseArguments<LlmProbeCommandOptions>(
            ["--provider", "openai", "--raw-reasoning", "/tmp/fragments.json"]);

        var options = Assert.IsType<CommandLine.Parsed<LlmProbeCommandOptions>>(parsed).Value;
        Assert.Equal("/tmp/fragments.json", options.RawReasoning);
    }

    [Fact]
    public void ShouldParseTheRawReasoningFile_IntoLabelledBodies()
    {
        var ok = LlmCommand.TryParseRawReasoning(
            """[{"label":"effort:xhigh","body":{"reasoning_effort":"xhigh"}},{"label":"off","body":{}}]""",
            out var fragments, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(["effort:xhigh", "off"], fragments.Select(f => f.Label).ToArray());
        Assert.Equal("xhigh", (string?)fragments[0].Body["reasoning_effort"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"label":"x","body":{}}""")]
    [InlineData("""[{"body":{}}]""")]
    [InlineData("""[{"label":" ","body":{}}]""")]
    [InlineData("""[{"label":3,"body":{}}]""")]
    [InlineData("""[{"label":"x"}]""")]
    [InlineData("""[{"label":"x","body":"effort"}]""")]
    [InlineData("""["x"]""")]
    public void ShouldRefuseAnInvalidRawReasoningFile(string json)
    {
        var ok = LlmCommand.TryParseRawReasoning(json, out var fragments, out var error);

        Assert.False(ok);
        Assert.Empty(fragments);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
