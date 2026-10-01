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

    [Fact]
    public void A_disabled_section_registers_nothing()
    {
        var services = Register(Settings(new() { ["RaggableTree:Enabled"] = "false" }));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(RaggableTreeOptions));
    }
}
