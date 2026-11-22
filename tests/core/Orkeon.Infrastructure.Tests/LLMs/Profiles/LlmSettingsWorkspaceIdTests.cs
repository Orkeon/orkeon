using Microsoft.Extensions.Configuration;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// #468: <c>WorkspaceId</c> of the <c>Llm</c> section and of each profile, read into
/// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig.WorkspaceId"/> so that every host
/// (run, orkeon-host, the forge, a crew) can use an Anthropic identity-linked key.
/// </summary>
public sealed class LlmSettingsWorkspaceIdTests
{
    private const string Workspace = "wrkspc_01TestWorkspaceIdentifier";

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    [Trait("BR", "CH-468")]
    public void ShouldReadTheWorkspace_OfTheDefaultSectionAndOfAProfile()
    {
        var configuration = Configuration(
            ("Llm:Model", "claude-opus-5-5"), ("Llm:WorkspaceId", Workspace),
            ("Llm:Profiles:large:Model", "claude-opus-5-5"), ("Llm:Profiles:large:WorkspaceId", " wrkspc_other "));

        Assert.Equal(Workspace, LlmSettings.ReadDefault(configuration).WorkspaceId);
        Assert.Equal("wrkspc_other", Assert.Single(LlmSettings.ReadProfiles(configuration)).Config.WorkspaceId);
    }

    [Fact]
    [Trait("BR", "CH-468")]
    public void ShouldReadNoWorkspace_WhenTheKeyIsAbsentOrBlank()
    {
        var configuration = Configuration(("Llm:Model", "m"), ("Llm:Profiles:a:Model", "m"), ("Llm:Profiles:a:WorkspaceId", " "));

        Assert.Null(LlmSettings.ReadDefault(configuration).WorkspaceId);
        Assert.Null(Assert.Single(LlmSettings.ReadProfiles(configuration)).Config.WorkspaceId);
    }

    /// <summary>The environment variable form is the binder's own: <c>Llm__WorkspaceId</c>.</summary>
    [Fact]
    [Trait("BR", "CH-468")]
    public void ShouldReadTheWorkspace_FromItsEnvironmentVariableForm()
    {
        const string Prefix = "ORKEON_TEST_468_";
        Environment.SetEnvironmentVariable(Prefix + "Llm__Model", "claude-opus-5-5");
        Environment.SetEnvironmentVariable(Prefix + "Llm__WorkspaceId", Workspace);
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(Prefix).Build();

            Assert.Equal(Workspace, LlmSettings.ReadDefault(configuration).WorkspaceId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Prefix + "Llm__Model", null);
            Environment.SetEnvironmentVariable(Prefix + "Llm__WorkspaceId", null);
        }
    }
}
