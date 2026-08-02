using System.Reflection;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Pins the default model and endpoint of every LLM provider (LLM-01).
/// </summary>
/// <remarks>
/// Changing a default silently changes the behaviour of every configuration that does not
/// specify a model or a base URL. These tests make such a change fail loudly, so it becomes
/// a deliberate decision visible in review rather than a drift nobody noticed. Each value
/// below was confirmed on the vendor's official documentation on 2026-07-27 — see
/// <c>backstage/features/drafts/LLM-PROVIDERS-TEST-MATRIX.md</c> §6.
/// </remarks>
public class ProviderDefaultsPinningTests
{
    private static readonly IHttpClientFactory HttpClientFactory = new TestHttpClientFactory();

    // ── Pinned values ───────────────────────────────────────────────────────

    public static TheoryData<string, string> PinnedDefaultModels() => new()
    {
        { nameof(OpenAIProvider), "gpt-5.6-sol" },
        { nameof(AzureOpenAILlmProvider), "gpt-5.6-sol" },
        { nameof(DeepSeekLlmProvider), "deepseek-v4-flash" },
        { nameof(KimiLlmProvider), "kimi-k2.6" },
        { nameof(QwenLlmProvider), "qwen3.7-plus" },
        { nameof(MistralLlmProvider), "mistral-medium-3-5-26-04" },
        { nameof(GroqLlmProvider), "llama-3.3-70b-versatile" },
        { nameof(TogetherAiLlmProvider), "meta-llama/Llama-3.3-70B-Instruct-Turbo" },
        { nameof(HuggingFaceLlmProvider), "meta-llama/Llama-3.1-8B-Instruct" },
        { nameof(ZaiLlmProvider), "glm-5.2" },
    };

    public static TheoryData<string, string> PinnedDefaultBaseUrls() => new()
    {
        { nameof(OpenAIProvider), "https://api.openai.com/v1" },
        { nameof(DeepSeekLlmProvider), "https://api.deepseek.com" },
        { nameof(KimiLlmProvider), "https://api.moonshot.ai/v1" },
        { nameof(QwenLlmProvider), "https://dashscope.aliyuncs.com/compatible-mode/v1" },
        { nameof(MistralLlmProvider), "https://api.mistral.ai/v1" },
        { nameof(GroqLlmProvider), "https://api.groq.com/openai/v1" },
        { nameof(TogetherAiLlmProvider), "https://api.together.xyz/v1" },
        { nameof(HuggingFaceLlmProvider), "https://router.huggingface.co/v1" },
        { nameof(ZaiLlmProvider), "https://api.z.ai/api/paas/v4" },
    };

    // ── The two providers outside OpenAICompatibleProviderBase ──────────────

    [Fact]
    public void ShouldPinAnthropicDefaults()
    {
        Assert.Equal("https://api.anthropic.com", AnthropicLlmProvider.DefaultBaseUrl);
        Assert.Equal("claude-sonnet-5", ProviderDefaults.AnthropicDefaults.DefaultModel);
    }

    [Fact]
    public void ShouldPinOllamaDefaults()
    {
        Assert.Equal("http://localhost:11434", LlmEndpoints.OllamaDefault);
        Assert.Equal("llama3.2", ProviderDefaults.OllamaDefaults.DefaultModel);
    }

    [Fact]
    public void ShouldPinPlatformWideDefaultModel()
    {
        // LlmConfig.Model falls back to this when nothing is configured anywhere.
        Assert.Equal("gpt-5.6-sol", LlmDefaults.DefaultModelName);
        Assert.Equal(LlmDefaults.DefaultModelName, LlmConfig.Default().Model);
    }

    // ── The ten OpenAI-compatible providers ─────────────────────────────────

    [Theory]
    [MemberData(nameof(PinnedDefaultModels))]
    public void ShouldPinDefaultModel_ForEachOpenAiCompatibleProvider(string providerTypeName, string expectedModel)
    {
        var actual = ReadProtectedMember<string>(providerTypeName, "DefaultModel");
        Assert.Equal(expectedModel, actual);
    }

    [Theory]
    [MemberData(nameof(PinnedDefaultBaseUrls))]
    public void ShouldPinDefaultBaseUrl_ForEachOpenAiCompatibleProvider(string providerTypeName, string expectedBaseUrl)
    {
        var actual = ReadProtectedMember<Uri>(providerTypeName, "DefaultBaseUrl");
        Assert.Equal(expectedBaseUrl, actual.ToString().TrimEnd('/'));
    }

    /// <summary>
    /// Azure deliberately has no provider-wide endpoint: the resource URL must come from the
    /// configuration, which is why every entry point validates it (see the D-01 guard tests).
    /// </summary>
    [Fact]
    public void ShouldNotPinAnAzureEndpoint_BecauseTheResourceUrlIsPerAccount()
    {
        var baseUrl = ReadProtectedMember<Uri>(nameof(AzureOpenAILlmProvider), "DefaultBaseUrl");
        Assert.Equal("about:blank", baseUrl.ToString());
    }

    // ── The provider-key lookup ─────────────────────────────────────────────

    public static TheoryData<string, string> PinnedDefaultsByProviderKey() => new()
    {
        { "openai", "gpt-5.6-sol" },
        { "anthropic", "claude-sonnet-5" },
        { "ollama", "llama3.2" },
        { "groq", "llama-3.3-70b-versatile" },
        { "together", "meta-llama/Llama-3.3-70B-Instruct-Turbo" },
        { "togetherai", "meta-llama/Llama-3.3-70B-Instruct-Turbo" },
        { "deepseek", "deepseek-v4-flash" },
        { "kimi", "kimi-k2.6" },
        { "moonshot", "kimi-k2.6" },
        { "qwen", "qwen3.7-plus" },
        { "mistral", "mistral-medium-3-5-26-04" },
        { "huggingface", "meta-llama/Llama-3.1-8B-Instruct" },
        { "hf", "meta-llama/Llama-3.1-8B-Instruct" },
        { "zai", "glm-5.2" },
        { "glm", "glm-5.2" },
        { "zhipu", "glm-5.2" },
    };

    /// <summary>
    /// A caller holding only a provider key must get that provider's default, not the
    /// platform-wide one — otherwise a campaign named "groq" quietly measures an OpenAI model.
    /// </summary>
    [Theory]
    [MemberData(nameof(PinnedDefaultsByProviderKey))]
    public void ShouldResolveTheDefaultModel_FromTheProviderKeyUsedByTheFactory(
        string providerKey, string expectedModel)
    {
        Assert.Equal(expectedModel, Infrastructure.Constants.Llm.ProviderDefaults.ForProvider(providerKey));
    }

    /// <summary>Azure serves deployments an operator named; there is nothing to default to.</summary>
    [Theory]
    [InlineData("azure")]
    [InlineData("azure-openai")]
    [InlineData("not-a-provider")]
    public void ShouldResolveNoDefaultModel_ForProvidersThatHaveNone(string providerKey)
    {
        Assert.Null(Infrastructure.Constants.Llm.ProviderDefaults.ForProvider(providerKey));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a <c>protected</c> default (DefaultModel / DefaultBaseUrl) from a live provider
    /// instance. Reflection is deliberate: it pins what the provider actually resolves at
    /// runtime, not merely the value of a constant the provider might stop referencing.
    /// </summary>
    private static T ReadProtectedMember<T>(string providerTypeName, string memberName)
    {
        var provider = CreateProvider(providerTypeName);
        try
        {
            var property = provider.GetType().GetProperty(
                memberName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(property);
            return Assert.IsType<T>(property.GetValue(provider));
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Builds a provider with a default configuration — the exact situation the defaults exist
    /// for. No HTTP call is made; only the default properties are read.
    /// </summary>
    /// <remarks>
    /// The constructor is selected by predicate rather than through
    /// <see cref="Activator.CreateInstance(Type, object?[])"/>: every provider offers several
    /// overloads that differ only in their third parameter (logger / resilience policy /
    /// tool-calling strategy), so a bare <c>null</c> argument is ambiguous. The tool-calling
    /// overload is picked and every optional argument after the factory is passed as null.
    /// </remarks>
    private static object CreateProvider(string providerTypeName)
    {
        var type = typeof(OpenAIProvider).Assembly.GetType(
            $"Orkeon.Infrastructure.LLMs.{providerTypeName}", throwOnError: true)!;

        var ctor = Array.Find(
            type.GetConstructors(),
            c => c.GetParameters() is [var config, var factory, var strategy, ..]
                 && config.ParameterType == typeof(LlmConfig)
                 && factory.ParameterType == typeof(IHttpClientFactory)
                 && strategy.ParameterType == typeof(IToolCallingStrategy));
        Assert.NotNull(ctor);

        var args = new object?[ctor.GetParameters().Length];
        args[0] = LlmConfig.Default();
        args[1] = HttpClientFactory;
        return ctor.Invoke(args);
    }
}
