using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Rag.WebFallback;

namespace Orkeon.Rag.Tests.WebFallback;

/// <summary>
/// Tests for <see cref="WebFallbackExtensions.AddOrkeonRagWebFallback"/> —
/// the strict opt-in registration of the secure web fallback (RAG-06/C1,
/// TryAdd, host wins, transport options bound on <c>Orkeon:Rag:WebFallback</c>)
/// and the conditional <see cref="IWebDocumentRetriever"/> adapter registration
/// bridging it into the corrective graph (RAG-06/6D).
/// </summary>
public class WebFallbackExtensionsTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    [Fact]
    public void AddOrkeonRagWebFallback_RegistersTheRetriever()
    {
        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<WebSearchDocumentRetriever>());
        Assert.NotNull(provider.GetRequiredService<PromptInjectionDocumentValidator>());
    }

    [Fact]
    public void AddOrkeonRagWebFallback_BindsOptions_FromTheSection()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Orkeon:Rag:WebFallback:Enabled"] = "true",
            ["Orkeon:Rag:WebFallback:Endpoint"] = "https://searx.local/search",
            ["Orkeon:Rag:WebFallback:ApiKeyEnvVar"] = "SEARX_API_KEY",
            ["Orkeon:Rag:WebFallback:MaxResults"] = "5",
            ["Orkeon:Rag:WebFallback:Timeout"] = "00:00:07",
            ["Orkeon:Rag:WebFallback:SuspiciousAction"] = "Discard",
        });

        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<WebSearchRetrieverOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal("https://searx.local/search", options.Endpoint);
        Assert.Equal("SEARX_API_KEY", options.ApiKeyEnvVar);
        Assert.Equal(5, options.MaxResults);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Timeout);
        Assert.Equal(SuspiciousContentAction.Discard, options.SuspiciousAction);
    }

    [Fact]
    public void AddOrkeonRagWebFallback_Defaults_AreStrictOptIn()
    {
        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<WebSearchRetrieverOptions>>().Value;
        Assert.False(options.Enabled);
        Assert.Equal("", options.Endpoint);
        Assert.Equal(3, options.MaxResults);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
        Assert.Equal(SuspiciousContentAction.Flag, options.SuspiciousAction);
    }

    [Fact]
    public void AddOrkeonRagWebFallback_HostRegisteredRetrieverWins()
    {
        var services = new ServiceCollection();
        var hostRetriever = new WebSearchDocumentRetriever(
            new FakeHttpClientFactory(new NoOpHandler()),
            Microsoft.Extensions.Options.Options.Create(new WebSearchRetrieverOptions()),
            new PromptInjectionDocumentValidator());
        services.AddSingleton(hostRetriever);

        services.AddOrkeonRagWebFallback(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        Assert.Same(hostRetriever, provider.GetRequiredService<WebSearchDocumentRetriever>());
    }

    // ── IWebDocumentRetriever adapter (RAG-06/6D) ──────────────────────────

    [Fact]
    public void Adapter_IsNotRegistered_ByDefault_TheGraphEdgeStaysSkipped()
    {
        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IWebDocumentRetriever>());
    }

    [Fact]
    public void Adapter_IsNotRegistered_WhenEnabledWithoutEndpoint()
    {
        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Orkeon:Rag:WebFallback:Enabled"] = "true",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IWebDocumentRetriever>());
    }

    [Fact]
    public void Adapter_IsRegistered_WhenEnabledAndConfigured_AndDelegatesToTheRetriever()
    {
        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Orkeon:Rag:WebFallback:Enabled"] = "true",
            ["Orkeon:Rag:WebFallback:Endpoint"] = "https://searx.local/search",
        }));
        using var provider = services.BuildServiceProvider();

        var adapter = Assert.IsType<WebSearchDocumentRetrieverAdapter>(
            provider.GetRequiredService<IWebDocumentRetriever>());
        Assert.NotNull(adapter);
    }

    [Fact]
    public void Adapter_HostRegisteredWebDocumentRetrieverWins()
    {
        var services = new ServiceCollection();
        var hostRetriever = new StubWebDocumentRetriever();
        services.AddSingleton<IWebDocumentRetriever>(hostRetriever);

        services.AddOrkeonRagWebFallback(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Orkeon:Rag:WebFallback:Enabled"] = "true",
            ["Orkeon:Rag:WebFallback:Endpoint"] = "https://searx.local/search",
        }));
        using var provider = services.BuildServiceProvider();

        Assert.Same(hostRetriever, provider.GetRequiredService<IWebDocumentRetriever>());
    }

    [Fact]
    public async Task Adapter_Delegates_ToTheConcreteRetriever()
    {
        // Transport disabled → the concrete retriever answers with an empty list
        // without any HTTP call: the delegation itself is what is verified here.
        var retriever = new WebSearchDocumentRetriever(
            new FakeHttpClientFactory(new NoOpHandler()),
            Microsoft.Extensions.Options.Options.Create(new WebSearchRetrieverOptions()),
            new PromptInjectionDocumentValidator());
        var adapter = new WebSearchDocumentRetrieverAdapter(retriever);

        var documents = await adapter.SearchAsync("q", 3, TestContext.Current.CancellationToken);

        Assert.Empty(documents);
    }

    [Fact]
    public void Adapter_Ctor_GuardsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WebSearchDocumentRetrieverAdapter(null!));
    }

    // ── options reconciliation (RAG-06/6D) ─────────────────────────────────

    [Fact]
    public void TheTwoWebFallbackSections_BindIndependently_PipelinePolicyVsTransport()
    {
        // One configuration, two deliberate sections: the pipeline-side policy
        // (Abstractions RagWebFallbackOptions on Orkeon:Rag:Corrective:WebFallback)
        // and the transport (WebSearchRetrieverOptions on Orkeon:Rag:WebFallback).
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Orkeon:Rag:Corrective:WebFallback:Enabled"] = "true",
            ["Orkeon:Rag:Corrective:WebFallback:MaxResults"] = "2",
            ["Orkeon:Rag:WebFallback:Enabled"] = "false",
            ["Orkeon:Rag:WebFallback:MaxResults"] = "9",
            ["Orkeon:Rag:WebFallback:Endpoint"] = "https://searx.local/search",
        });

        var services = new ServiceCollection();
        services.AddOrkeonRagWebFallback(configuration);
        using var provider = services.BuildServiceProvider();

        // Transport side: bound from Orkeon:Rag:WebFallback only.
        var transport = provider.GetRequiredService<IOptions<WebSearchRetrieverOptions>>().Value;
        Assert.False(transport.Enabled);
        Assert.Equal(9, transport.MaxResults);
        Assert.Equal("https://searx.local/search", transport.Endpoint);

        // Transport disabled → no adapter, whatever the pipeline policy says.
        Assert.Null(provider.GetService<IWebDocumentRetriever>());

        // Pipeline side: bound from Orkeon:Rag(:Corrective:WebFallback) only.
        var pipelineOptions = Orkeon.Rag.Configuration.RagOptionsFactory.Build(configuration);
        Assert.True(pipelineOptions.Corrective.WebFallback.Enabled);
        Assert.Equal(2, pipelineOptions.Corrective.WebFallback.MaxResults);
    }

    [Fact]
    public void AddOrkeonRagWebFallback_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => WebFallbackExtensions.AddOrkeonRagWebFallback(null!, BuildConfiguration()));
        Assert.Throws<ArgumentNullException>(
            () => new ServiceCollection().AddOrkeonRagWebFallback(null!));
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
