using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// Behaviour tests for <see cref="LocalEmbedTool"/>. Uses a stub
/// <see cref="IEmbeddingProvider"/> to avoid the ~200 ms ONNX boot — these
/// tests exercise the tool's request/response pipeline only.
/// </summary>
public sealed class LocalEmbedToolTests
{
    /// <summary>
    /// Deterministic stub that returns a fixed-length vector per input and preserves order.
    /// </summary>
    private sealed class StubProvider : IEmbeddingProvider
    {
        public int Dimensions { get; init; } = 384;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct)
        {
            var result = new ReadOnlyMemory<float>[texts.Count];
            for (var i = 0; i < texts.Count; i++)
            {
                // Cheap, distinct, length-correct vector.
                var arr = new float[Dimensions];
                arr[0] = i + 1f;
                arr[1] = texts[i]?.Length ?? 0f;
                result[i] = arr;
            }
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(result);
        }
    }

    [Fact]
    public async Task LocalEmbedTool_Inputs_PreservesOrder_AndShape()
    {
        var provider = new StubProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions());

        var request = new LocalEmbedRequest
        {
            Inputs = ["first", "second", "third"]
        };

        // Use the protected ExecuteTypedAsync via a thin internal accessor: the
        // public CallAsync goes through the dictionary pipeline, but the typed
        // pipeline is the surface we actually want to exercise here.
        var response = await InvokeTypedAsync(tool, request);

        Assert.Equal(3, response.Embeddings.Count);
        Assert.Equal(384, response.Dimensions);
        Assert.Equal(384, response.Embeddings[0].Length);
        Assert.Equal(384, response.Embeddings[1].Length);
        Assert.Equal(384, response.Embeddings[2].Length);

        // Order preserved: stub uses the position index in slot[0].
        Assert.Equal(1f, response.Embeddings[0][0]);
        Assert.Equal(2f, response.Embeddings[1][0]);
        Assert.Equal(3f, response.Embeddings[2][0]);
    }

    [Fact]
    public async Task LocalEmbedTool_RequestedDimensions_Hint_IsIgnored_When_Unsupported()
    {
        var provider = new StubProvider { Dimensions = 384 };
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions());

        var request = new LocalEmbedRequest
        {
            Inputs = ["anything"],
            RequestedDimensions = 768, // BGE-micro-v2 doesn't support projection.
        };

        var response = await InvokeTypedAsync(tool, request);

        // The hint must be ignored, not raise an exception.
        Assert.Equal(384, response.Dimensions);
        Assert.Single(response.Embeddings);
        Assert.Equal(384, response.Embeddings[0].Length);
    }

    [Fact]
    public async Task LocalEmbedTool_TruncatedCount_Reflects_LongInputs()
    {
        var provider = new StubProvider();
        using var tool = new LocalEmbedTool(provider, new LocalEmbeddingOptions { MaxTextChars = 100 });

        var request = new LocalEmbedRequest
        {
            Inputs =
            [
                new string('x', 500), // truncated
                "tiny",                // not truncated
                new string('y', 200), // truncated
            ]
        };

        var response = await InvokeTypedAsync(tool, request);

        Assert.Equal(2, response.TruncatedCount);
        Assert.Equal(3, response.Embeddings.Count);
    }

    /// <summary>
    /// Helper that invokes the protected <c>ExecuteTypedAsync</c> via reflection so the
    /// test stays focused on the typed pipeline (no JSON deserialisation noise).
    /// </summary>
    private static async Task<LocalEmbedResponse> InvokeTypedAsync(LocalEmbedTool tool, LocalEmbedRequest request)
    {
        var method = typeof(LocalEmbedTool).GetMethod(
            "ExecuteTypedAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ExecuteTypedAsync not found");

        var task = (Task<LocalEmbedResponse>)method.Invoke(tool, [request, CancellationToken.None])!;
        return await task;
    }
}
