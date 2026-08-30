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
}
