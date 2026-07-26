using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Rag.WebFallback;

namespace Orkeon.Rag.Tests.WebFallback;

/// <summary>
/// Tests for <see cref="WebFallbackExtensions.AddOrkeonRagWebFallback"/> —
/// the strict opt-in registration of the secure web fallback (RAG-06/C1,
/// TryAdd, host wins, options bound on <c>Orkeon:Rag:WebFallback</c>).
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

        var options = provider.GetRequiredService<IOptions<RagWebFallbackOptions>>().Value;
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

        var options = provider.GetRequiredService<IOptions<RagWebFallbackOptions>>().Value;
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
            Microsoft.Extensions.Options.Options.Create(new RagWebFallbackOptions()),
            new PromptInjectionDocumentValidator());
        services.AddSingleton(hostRetriever);

        services.AddOrkeonRagWebFallback(BuildConfiguration());
        using var provider = services.BuildServiceProvider();

        Assert.Same(hostRetriever, provider.GetRequiredService<WebSearchDocumentRetriever>());
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
