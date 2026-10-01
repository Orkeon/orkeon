using Microsoft.Extensions.DependencyInjection;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Augmentation;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// Tests for <see cref="KnowledgeAugmentationExtensions.AddOrkeonKnowledgeAugmentation"/> —
/// the separate opt-in of the prompt knowledge augmentation (RAG-03/C4, TryAdd, host wins).
/// </summary>
public class KnowledgeAugmentationExtensionsTests
{
    [Fact]
    public void AddOrkeonKnowledgeAugmentation_RegistersTheDefaultAugmenter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRagProfileResolver>(new RecordingProfileResolver { Pipeline = new FakeRetrievalPipeline() });

        services.AddOrkeonKnowledgeAugmentation();
        using var provider = services.BuildServiceProvider();

        var augmenter = provider.GetRequiredService<IKnowledgeContextAugmenter>();
        Assert.IsType<KnowledgeContextAugmenter>(augmenter);
    }

    [Fact]
    public async Task AddOrkeonKnowledgeAugmentation_AnAttachmentWithoutProfileUsesTheHostProfile()
    {
        var resolver = new RecordingProfileResolver { Pipeline = new FakeRetrievalPipeline() };
        var services = new ServiceCollection();
        services.AddSingleton<IRagProfileResolver>(resolver);
        services.AddSingleton(new Orkeon.Rag.Abstractions.Options.RagOptions { Profile = "quality" });

        services.AddOrkeonKnowledgeAugmentation();
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IKnowledgeContextAugmenter>().BuildContextAsync(
            [Orkeon.Domain.Knowledge.KnowledgeAttachment.Create("docs")], "query",
            TestContext.Current.CancellationToken);

        Assert.Equal(["quality"], resolver.ResolvedProfiles);
    }

    [Fact]
    public void AddOrkeonKnowledgeAugmentation_HostRegisteredAugmenterWins()
    {
        var services = new ServiceCollection();
        var hostAugmenter = new HostAugmenter();
        services.AddSingleton<IKnowledgeContextAugmenter>(hostAugmenter);

        services.AddOrkeonKnowledgeAugmentation();
        using var provider = services.BuildServiceProvider();

        Assert.Same(hostAugmenter, provider.GetRequiredService<IKnowledgeContextAugmenter>());
    }

    [Fact]
    public void AddOrkeonKnowledgeAugmentation_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => KnowledgeAugmentationExtensions.AddOrkeonKnowledgeAugmentation(null!));
    }

    private sealed class HostAugmenter : IKnowledgeContextAugmenter
    {
        public Task<Orkeon.Rag.Abstractions.Models.KnowledgeContextBlock?> BuildContextAsync(
            IReadOnlyList<Orkeon.Domain.Knowledge.KnowledgeAttachment> attachments,
            string taskInput,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Orkeon.Rag.Abstractions.Models.KnowledgeContextBlock?>(null);
    }
}
