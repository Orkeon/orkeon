namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// <c>orkeon llm models --workspace-id</c> (#468): the listing builds no provider, so a provider other than
/// Anthropic, which is not sent the id, is named on stderr rather than dropping the option in silence.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class LlmModelsCommandTests
{
    [Fact]
    [Trait("BR", "CH-468")]
    public async Task ShouldWarnThatTheIdWasNotSent_WhenAProviderOtherThanAnthropicIsGivenAWorkspace()
    {
        using var catalogue = new CatalogueServer();
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await Program.DispatchAsync(console,
            ["llm", "models", "-p", "openai", "-u", catalogue.BaseUrl, "--workspace-id", "wrkspc_test"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("test-model", console.Stdout, StringComparison.Ordinal);
        Assert.Contains(
            "orkeon llm models: warning: option '--workspace-id' was declared but openai does not support it — it was not sent.",
            console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-468")]
    public async Task ShouldNotWarn_WhenNoWorkspaceIsGiven()
    {
        using var catalogue = new CatalogueServer();
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await Program.DispatchAsync(console, ["llm", "models", "-p", "openai", "-u", catalogue.BaseUrl]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.DoesNotContain("--workspace-id", console.Stderr, StringComparison.Ordinal);
    }
}
