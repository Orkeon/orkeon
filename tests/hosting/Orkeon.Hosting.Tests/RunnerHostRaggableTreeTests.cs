using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Analysis.DependencyInjection;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-15: the runner's <c>RaggableTree</c> section carries <c>Enabled</c> and <c>Embedding</c>,
/// nothing else. <c>Exclude</c>, <c>RootAlias</c>, <c>IndexMode</c>, <c>EnrichWithLlm</c> and
/// <c>IncludeStatements</c> were read, then ignored: every <c>index_codebase</c> call sets its own.
/// A host that still carries one fails at startup, naming the key, instead of indexing
/// <c>vendor/</c> after being told not to.
/// </summary>
public sealed class RunnerHostRaggableTreeTests
{
    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceCollection Register(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };
        RunnerHost.RegisterRaggableTree(context, services);
        return services;
    }

    [Fact]
    public void Enabled_and_Embedding_are_read()
    {
        var services = Register(Settings(new()
        {
            ["RaggableTree:Enabled"] = "true",
            ["RaggableTree:Embedding:Provider"] = "None",
        }));

        var options = Assert.Single(services, d => d.ServiceType == typeof(RaggableTreeOptions))
            .ImplementationInstance as RaggableTreeOptions;
        Assert.NotNull(options);
        Assert.Equal(EmbeddingProviderKind.None, options.Embedding.Provider);
    }

    [Theory]
    [InlineData("Exclude:0", "vendor")]
    [InlineData("RootAlias", "app")]
    [InlineData("IndexMode", "Live")]
    [InlineData("EnrichWithLlm", "true")]
    [InlineData("IncludeStatements", "true")]
    public void A_retired_key_fails_the_host_and_names_the_key(string key, string value)
    {
        var configuration = Settings(new()
        {
            ["RaggableTree:Embedding:Provider"] = "None",
            ["RaggableTree:" + key] = value,
        });

        var ex = Assert.Throws<InvalidOperationException>(() => Register(configuration));

        var name = key.Split(':')[0];
        Assert.Contains("RaggableTree:" + name, ex.Message, StringComparison.Ordinal);
        Assert.Contains("index_codebase", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40, decision 4 — a value of <c>Embedding</c> the host cannot read refuses the start: a
    /// provider written wrong ran the local embeddings, a number the enum parser took as well, and a
    /// dimension that was none fell back on the provider's own, all without a word.
    /// </summary>
    [Theory]
    [InlineData("Provider", "Olama", "RaggableTree:Embedding:Provider")]
    [InlineData("Provider", "7", "RaggableTree:Embedding:Provider")]
    [InlineData("Dimensions", "auto", "RaggableTree:Embedding:Dimensions")]
    [InlineData("MaxTextChars", "lots", "RaggableTree:Embedding:MaxTextChars")]
    public void An_embedding_value_the_host_cannot_read_is_refused_by_its_key(string key, string value, string named)
    {
        var configuration = Settings(new() { ["RaggableTree:Embedding:" + key] = value });

        var ex = Assert.Throws<InvalidOperationException>(() => Register(configuration));

        Assert.Contains(named, ex.Message, StringComparison.Ordinal);
        Assert.Contains(value, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_name_is_read_whatever_its_case()
    {
        var services = Register(Settings(new() { ["RaggableTree:Embedding:Provider"] = "ollama" }));

        var options = Assert.Single(services, d => d.ServiceType == typeof(RaggableTreeOptions))
            .ImplementationInstance as RaggableTreeOptions;
        Assert.Equal(EmbeddingProviderKind.Ollama, options!.Embedding.Provider);
    }

    [Fact]
    public void A_disabled_section_registers_nothing()
    {
        var services = Register(Settings(new() { ["RaggableTree:Enabled"] = "false" }));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(RaggableTreeOptions));
    }
}
