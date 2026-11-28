using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// #490: the conversation breakpoint follows the system-prompt and tools switches for a tool loop,
/// but the single-shot factory <see cref="LlmCacheConfig.SystemPrompt"/> keeps its old meaning - the
/// system prompt alone - so a one-call path never pays a conversation write it never reads.
/// </summary>
public sealed class LlmCacheConfigConversationTests
{
    [Fact]
    [Trait("BR", "CH-490")]
    public void ShouldCacheTheSystemPromptAlone_WhenBuiltBySystemPrompt()
    {
        var config = LlmCacheConfig.SystemPrompt();

        Assert.True(config.CacheSystemPrompt);
        Assert.False(config.CacheTools);
        Assert.False(config.CacheConversation);
    }

    [Fact]
    [Trait("BR", "CH-490")]
    public void ShouldCacheTheConversation_WhenARequestMarksTheSystemPromptOrTheTools()
    {
        Assert.True(LlmCacheConfig.Request(systemPrompt: true, tools: false, ttl: null)!.CacheConversation);
        Assert.True(LlmCacheConfig.Request(systemPrompt: false, tools: true, ttl: null)!.CacheConversation);
        Assert.True(new LlmCacheConfig { CacheSystemPrompt = true }.CacheConversation);
    }
}
