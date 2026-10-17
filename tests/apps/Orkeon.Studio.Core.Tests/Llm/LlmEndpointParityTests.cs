using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Tests.Shared.Llm;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>Studio says what the run builds, for every address of the shared table (GAP-53).</summary>
public sealed class LlmEndpointParityTests
{
    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Theory]
    [Trait("BR", "CH-08")]
    [MemberData(nameof(LlmEndpointCases.Rows), MemberType = typeof(LlmEndpointCases))]
    public async Task ShouldAgreeWithTheRun_ForEachAddressOfTheTable(
        string address, string providerKey, string? ollamaRoot, bool isLocal)
    {
        _ = providerKey;
        _ = isLocal;
        var config = LlmConfig.OnProfile() with { BaseUrl = new Uri(address) };
        var built = new LlmProviderFactory(new DefaultHttpClientFactory(), NullLoggerFactory.Instance).Create(config).Name;
        var runIsOllama = string.Equals(built, LlmProviderKeys.Ollama, StringComparison.OrdinalIgnoreCase);

        var card = LlmPresets.CardOf(new ModelProfile { Name = "setting", Provider = null, BaseUrl = address, Model = "m" });

        Assert.Equal(runIsOllama, string.Equals(card, LlmPresets.Ollama, StringComparison.Ordinal));
        Assert.Equal(!runIsOllama, LlmPresets.NeedsApiKey(address));
        if (card is not (LlmPresets.Custom or LlmPresets.DockerModelRunner))
            Assert.Equal(built, card, ignoreCase: true);

        if (ollamaRoot is not null)
        {
            using var handler = new StubHttpMessageHandler { Body = """{ "models": [] }""" };
            using var client = new HttpClient(handler, disposeHandler: false);
            using var probe = new HttpLlmEndpointProbe(client);

            await probe.ProbeAsync(new LlmProbeRequest { BaseUrl = address }, TestContext.Current.CancellationToken);

            Assert.Equal(new Uri(ollamaRoot.TrimEnd('/') + "/api/tags"), handler.LastRequest.Uri);
        }
    }
}
