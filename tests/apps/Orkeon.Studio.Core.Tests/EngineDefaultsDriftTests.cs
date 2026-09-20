using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Logging;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-22 — the defaults the settings screen shows as watermarks are copies of the engine's
/// (Studio Core does not reference the projects that own them); this test project does, and pins
/// every copy against the original, the ConstantDriftTests discipline.
/// </summary>
public sealed partial class EngineDefaultsDriftTests
{
    [Fact]
    public void The_rate_limiting_defaults_are_the_engines()
    {
        var engine = new RateLimitingOptions();

        Assert.Equal(engine.MaxConcurrentRequests, RateLimitingSection.DefaultMaxConcurrentRequests);
        Assert.Equal(engine.GlobalRequestsPerMinute, RateLimitingSection.DefaultGlobalRequestsPerMinute);
        Assert.Equal(engine.ProviderRequestsPerMinute, RateLimitingSection.DefaultProviderRequestsPerMinute);
        Assert.Equal(engine.AgentRequestsPerMinute, RateLimitingSection.DefaultAgentRequestsPerMinute);
        Assert.Equal(engine.QueueLimit, RateLimitingSection.DefaultQueueLimit);
    }

    [Fact]
    public void The_llm_logging_defaults_are_the_engines()
    {
        var engine = LlmLoggingOptions.Default;

        Assert.Equal(engine.FullEmbeddingLog, LlmLoggingSection.DefaultFullEmbeddingLog);
        Assert.Equal(engine.LogStreamingExchanges, LlmLoggingSection.DefaultLogStreamingExchanges);
        Assert.Equal(engine.MaxBodyLengthChars, LlmLoggingSection.DefaultMaxBodyLengthChars);
    }

    [Fact]
    public void The_rag_defaults_are_the_contracts()
    {
        Assert.Equal(RagDefaults.DefaultProfile, RagSection.DefaultProfile);
        Assert.Equal(new RagOptions().Profile, RagSection.DefaultProfile);
        Assert.Equal(RagCorrectiveOptions.DefaultMaxIterations, RagSection.DefaultCorrectiveMaxIterations);
        Assert.Equal(new RagCorrectiveOptions().MaxIterations, RagSection.DefaultCorrectiveMaxIterations);
        Assert.Equal(new RagHybridOptions().Enabled, RagSection.DefaultHybridRetrievalEnabled);
        Assert.Equal(new RagWebFallbackOptions().Enabled, RagSection.DefaultCorrectiveWebFallbackEnabled);
    }

    /// <summary>
    /// The transport half of the web fallback lives in <c>Orkeon.Rag</c>, which neither Studio Core
    /// nor this test project references: its default is read off the source, where an <c>Enabled</c>
    /// auto-property without an initializer is the off the section copies.
    /// </summary>
    [Fact]
    public void The_web_fallback_transport_default_is_off_in_the_source()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "rag", "Orkeon.Rag", "WebFallback", "WebSearchRetrieverOptions.cs"));

        var enabled = Assert.Single(EnabledPropertyPattern().Matches(source).Cast<Match>());
        Assert.Equal("", enabled.Groups[1].Value.Trim());
        Assert.False(RagSection.DefaultWebFallbackEnabled);
    }

    [GeneratedRegex(@"public bool Enabled \{ get; set; \}([^;\n]*);?")]
    private static partial Regex EnabledPropertyPattern();

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}
