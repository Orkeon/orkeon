using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Capabilities;

/// <summary>
/// #359 and #360: a provider tells what a model takes for reasoning, and what becomes of the effort a call asks
/// for, by the judgement it applies to the call itself.
/// </summary>
public class ReasoningReportTests
{
    private static readonly string OkBody = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 },
    });

    private static readonly string[] GlmLevels = ["low", "high", "max"];

    private static LlmConfig Config(string model, string? effort = null, bool? enabled = null) =>
        LlmConfig.Create(model, TestApiKey) with
        {
            BaseUrl = new Uri("https://provider.test/v1"),
            Thinking = effort is null && enabled is null ? null : new LlmThinkingConfig { Enabled = enabled, Effort = effort },
        };

    private static ILlmProvider Create(string provider, LlmConfig config, TestHttpClientFactory? http = null)
    {
        var factory = new LlmProviderFactory(
            http ?? new TestHttpClientFactory(), NullLoggerFactory.Instance, LlmProviderFactory.MountlessFileSystem());
        return ((LlmProviderAdapter)factory.Create(provider, config)).UnderlyingProvider;
    }

    [Fact]
    [Trait("BR", "CH-359")]
    public void ShouldListTheLevelsTheModelTakes_WhenTheTableNarrowsTheProvider()
    {
        var provider = Create("zai", Config("glm-5.3"));

        var capabilities = provider.DescribeReasoning(null);

        Assert.NotNull(capabilities);
        Assert.Equal("glm-5.3", capabilities.Model);
        Assert.True(capabilities.Known);
        Assert.Equal(ThinkingSupport.EffortOnly, capabilities.Thinking);
        Assert.Equal(GlmLevels, capabilities.Efforts);
    }

    [Fact]
    [Trait("BR", "CH-359")]
    public void ShouldListNoLevel_WhenTheModelTakesNoEffort()
    {
        var provider = Create("openai", Config("o1-mini"));

        var capabilities = provider.DescribeReasoning("o1-mini");

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Known);
        Assert.Empty(capabilities.Efforts);
    }

    [Fact]
    [Trait("BR", "CH-359")]
    public void ShouldListTheProviderLevelsAsUnknown_WhenTheTableDoesNotKnowTheModel()
    {
        var provider = Create("openai", Config("house-model-7"));

        var capabilities = provider.DescribeReasoning("house-model-7");

        Assert.NotNull(capabilities);
        Assert.False(capabilities.Known);
        Assert.Equal(provider.Capabilities.EffortLevels, capabilities.Efforts);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public async Task ShouldReportTheLevelSentOnTheWire_WhenTheModelLowersIt()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var http = new TestHttpClientFactory();
        http.RegisterClient(nameof(ZaiLlmProvider), new HttpClient(handler));
        var config = Config("glm-5.3", "medium");
        var provider = Create("zai", config, http);

        var report = provider.ReportEffort(config);
        await provider.ChatAsync([LlmMessage.User("hi")], config, TestContext.Current.CancellationToken);

        Assert.Equal(new LlmEffortReport("glm-5.3", "medium", "low", LlmEffortNotice.Lowered), report);
        Assert.NotNull(report);
        var raw = await Assert.Single(handler.CapturedRequests).Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(report.Sent, body.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldReportTheEffortDropped_WhenTheModelTakesNone()
    {
        var provider = Create("openai", Config("o1-mini"));

        var report = provider.ReportEffort(Config("o1-mini", "high"));

        Assert.Equal(new LlmEffortReport("o1-mini", "high", null, LlmEffortNotice.Dropped), report);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldReportTheEffortUnchecked_WhenTheTableDoesNotKnowTheModel()
    {
        var provider = Create("openai", Config("house-model-7"));

        var report = provider.ReportEffort(Config("house-model-7", "high"));

        Assert.Equal(new LlmEffortReport("house-model-7", "high", "high", LlmEffortNotice.Unchecked), report);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldReportNoNotice_WhenTheModelTakesTheLevel()
    {
        var provider = Create("openai", Config("o3-mini"));

        var report = provider.ReportEffort(Config("o3-mini", "high"));

        Assert.Equal(new LlmEffortReport("o3-mini", "high", "high", null), report);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldJudgeTheHostEffort_WhenTheCallSetsNone()
    {
        // The host's Llm:Thinking:Effort lives on the provider's own configuration; the call names a model only.
        var provider = Create("zai", Config("glm-5.3", "xhigh"));

        var report = provider.ReportEffort(LlmConfig.OnProfile());

        Assert.Equal(new LlmEffortReport("glm-5.3", "xhigh", "high", LlmEffortNotice.Lowered), report);
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldReportNothing_WhenNoEffortIsAsked()
    {
        var provider = Create("zai", Config("glm-5.3"));

        Assert.Null(provider.ReportEffort(Config("glm-5.3", enabled: true)));
        Assert.Null(provider.ReportEffort(null));
    }

    [Fact]
    [Trait("BR", "CH-360")]
    public void ShouldReportTheEffortDropped_WhenOllamaThinkingIsSwitchedOff()
    {
        // Ollama's `think` holds one value: "off" wins over an effort asked beside it.
        var provider = Create("ollama", Config("qwen3:8b"));

        var report = provider.ReportEffort(Config("qwen3:8b", "high", enabled: false));

        Assert.NotNull(report);
        Assert.Null(report.Sent);
        Assert.Equal(LlmEffortNotice.Dropped, report.Notice);
    }

    [Fact]
    [Trait("BR", "CH-359")]
    [Trait("BR", "CH-360")]
    public void ShouldAnswerForTheWrappedProvider_WhenMetered()
    {
        var inner = Create("zai", Config("glm-5.3"));
        var metered = MeteredLlmProvider.Wrap(inner, new MockLlmUsageSink());

        Assert.NotSame(inner, metered);
        Assert.Equal(inner.DescribeReasoning(null)!.Efforts, metered.DescribeReasoning(null)!.Efforts);
        Assert.Equal(inner.ReportEffort(Config("glm-5.3", "medium")), metered.ReportEffort(Config("glm-5.3", "medium")));
    }
}
