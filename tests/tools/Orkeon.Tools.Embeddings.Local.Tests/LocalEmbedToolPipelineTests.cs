using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// Pipeline + edge-case tests for <see cref="LocalEmbedTool"/>. Every test uses a
/// fully in-memory <see cref="IEmbeddingProvider"/> stub so no ONNX session is booted
/// — the tool's request/response surface is the only thing under test here.
/// </summary>
public sealed class LocalEmbedToolPipelineTests
{
    /// <summary>
    /// Deterministic stub: returns a distinct vector per input and records the inputs it saw.
    /// </summary>
    private sealed class RecordingProvider : IEmbeddingProvider
    {
        public int Dimensions { get; init; } = 384;
        public IReadOnlyList<string>? LastTexts { get; private set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            LastTexts = texts;
            ct.ThrowIfCancellationRequested();
            var result = new ReadOnlyMemory<float>[texts.Count];
            for (var i = 0; i < texts.Count; i++)
            {
                var arr = new float[Dimensions];
                arr[0] = i + 1f;
                result[i] = arr;
            }
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(result);
        }
    }

    private sealed class ThrowingProvider : IEmbeddingProvider
    {
        public int Dimensions => 384;
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct)
            => throw new InvalidOperationException("provider failed");
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new LocalEmbedTool(null!, new LocalEmbeddingOptions()));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new LocalEmbedTool(new RecordingProvider(), null!));
    }

    [Fact]
    public void Name_And_Description_AreStable()
    {
        using var tool = new LocalEmbedTool(new RecordingProvider(), new LocalEmbeddingOptions());

        Assert.Equal("local_embed_text", tool.Name);
        Assert.Contains("BGE-micro-v2", tool.Description);
    }

    [Fact]
    public void ExecuteTyped_NullRequest_Throws()
    {
        using var tool = new LocalEmbedTool(new RecordingProvider(), new LocalEmbeddingOptions());

        var method = typeof(LocalEmbedTool).GetMethod(
            "ExecuteTypedAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // The null guard runs synchronously (before the Task is returned), so reflection
        // surfaces it wrapped in TargetInvocationException — unwrap to assert the real cause.
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method.Invoke(tool, [null, CancellationToken.None]));
        Assert.IsType<ArgumentNullException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteTyped_NullInputs_ReturnsEmpty_WithoutCallingProvider()
    {
        var provider = new RecordingProvider { Dimensions = 384 };
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions());

        var response = await InvokeTypedAsync(tool, new LocalEmbedRequest { Inputs = null! });

        Assert.Empty(response.Embeddings);
        Assert.Equal(384, response.Dimensions);
        Assert.Equal("bge-micro-v2", response.Model);
        Assert.Equal(0, response.TruncatedCount);
        Assert.Equal(0, provider.Calls); // short-circuited
    }

    [Fact]
    public async Task ExecuteTyped_EmptyInputs_ReturnsEmpty_WithoutCallingProvider()
    {
        var provider = new RecordingProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions());

        var response = await InvokeTypedAsync(tool, new LocalEmbedRequest { Inputs = [] });

        Assert.Empty(response.Embeddings);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ExecuteTyped_TruncationCapDisabled_CountsZero()
    {
        // MaxTextChars null → CountTruncated must short-circuit to 0 regardless of length.
        var provider = new RecordingProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions { MaxTextChars = null });

        var response = await InvokeTypedAsync(tool, new LocalEmbedRequest
        {
            Inputs = [new string('x', 100_000)]
        });

        Assert.Equal(0, response.TruncatedCount);
        Assert.Single(response.Embeddings);
    }

    [Fact]
    public async Task ExecuteTyped_TruncationCapNonPositive_CountsZero()
    {
        var provider = new RecordingProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions { MaxTextChars = 0 });

        var response = await InvokeTypedAsync(tool, new LocalEmbedRequest
        {
            Inputs = [new string('x', 5000)]
        });

        Assert.Equal(0, response.TruncatedCount);
    }

    [Fact]
    public async Task ExecuteTyped_NullEntryInInputs_NotCountedAsTruncated()
    {
        // A null element must not throw and must not be counted as truncated.
        var provider = new RecordingProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions { MaxTextChars = 10 });

        var response = await InvokeTypedAsync(tool, new LocalEmbedRequest
        {
            Inputs = [null!, new string('y', 50)]
        });

        Assert.Equal(1, response.TruncatedCount); // only the long string
    }

    [Fact]
    public async Task ExecuteTyped_ReturnsDefensiveCopies()
    {
        // The tool must materialise float[] copies, not alias the provider's buffer.
        var provider = new RecordingProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions());

        var response = await InvokeTypedAsync(tool, new LocalEmbedRequest { Inputs = ["a", "b"] });

        Assert.Equal(2, response.Embeddings.Count);
        Assert.IsType<float[]>(response.Embeddings[0]);
        Assert.Equal(1f, response.Embeddings[0][0]);
        Assert.Equal(2f, response.Embeddings[1][0]);
    }

    [Fact]
    public async Task ExecuteTyped_ProviderThrows_Propagates()
    {
        // The tool does not swallow provider faults; they bubble up from ExecuteTypedCoreAsync.
        using var tool = new LocalEmbedTool(new ThrowingProvider(), new LocalEmbeddingOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeTypedAsync(tool, new LocalEmbedRequest { Inputs = ["x"] }));
    }

    [Fact]
    public async Task ExecuteTyped_ForwardsCancellationToken_ToProvider()
    {
        using var tool = new LocalEmbedTool(new RecordingProvider(), new LocalEmbeddingOptions());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var method = typeof(LocalEmbedTool).GetMethod(
            "ExecuteTypedAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var task = (Task<LocalEmbedResponse>)method.Invoke(
            tool, [new LocalEmbedRequest { Inputs = ["x"] }, cts.Token])!;

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void Schema_IsGenerated_FromRequestAndResponse()
    {
        using var tool = new LocalEmbedTool(new RecordingProvider(), new LocalEmbeddingOptions());

        var schema = tool.Schema;

        Assert.Equal("local_embed_text", schema.Name);
        Assert.NotNull(schema);
    }

    private static async Task<LocalEmbedResponse> InvokeTypedAsync(
        LocalEmbedTool tool, LocalEmbedRequest request)
    {
        var method = typeof(LocalEmbedTool).GetMethod(
            "ExecuteTypedAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ExecuteTypedAsync not found");

        var task = (Task<LocalEmbedResponse>)method.Invoke(tool, [request, CancellationToken.None])!;
        return await task;
    }
}
