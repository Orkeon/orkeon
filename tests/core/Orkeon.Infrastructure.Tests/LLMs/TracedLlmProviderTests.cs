using System.Diagnostics;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Telemetry;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// CH-08 - every call through the providers' entrance opens one <c>chat {model}</c> client span on the
/// Llm source, none when the current activity is already a chat span.
/// </summary>
public sealed class TracedLlmProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmMessage[] Messages => [LlmMessage.User("hello")];

    private static LlmConfig Config() => LlmConfig.Create("m-1") with { Temperature = 0.3, MaxTokens = 128 };

    private static IReadOnlyList<Activity> Chats(ActivityRecorder recorder, ActivityTraceId trace) =>
        [.. recorder.Snapshot(trace).Where(s => s.Source == OrkeonActivitySources.Llm && s.OperationName.StartsWith("chat", StringComparison.Ordinal))];

#pragma warning disable CA2000 // the test disposes the root it takes
    private static Activity StartRoot()
    {
        var root = new Activity("test.root");
        root.Start();
        return root;
    }
#pragma warning restore CA2000

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldOpenClientChatSpan_WhenProviderIsCalledOutsideAnySpan()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = StartRoot();
        var inner = new MockLlmProvider { Name = "OpenAI" };
        inner.SetChatResult(new LlmResponse { Content = "ok", PromptTokens = 7, CompletionTokens = 5, TokensUsed = 12 });
        var provider = TracedLlmProvider.Wrap(inner);

        using (LlmUsageScope.Begin(LlmUsageOperations.Manager, "crew-1", "Chef", "task-9"))
            await provider.ChatAsync(Messages, Config(), Ct);

        var span = Assert.Single(Chats(recorder, root.TraceId));
        Assert.Equal("chat m-1", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("chat", span.GetTagItem(GenAiAttributes.OperationName)?.ToString());
        Assert.Equal("openai", span.GetTagItem(GenAiAttributes.ProviderName)?.ToString());
        Assert.Equal("m-1", span.GetTagItem(GenAiAttributes.RequestModel)?.ToString());
        Assert.Equal(7, Convert.ToInt32(span.GetTagItem(GenAiAttributes.UsageInputTokens)));
        Assert.Equal(5, Convert.ToInt32(span.GetTagItem(GenAiAttributes.UsageOutputTokens)));
        Assert.Equal("manager", span.GetTagItem("orkeon.llm.operation")?.ToString());
        Assert.Equal("Chef", span.GetTagItem(GenAiAttributes.AgentName)?.ToString());
        Assert.Equal("task-9", span.GetTagItem("orkeon.task.id")?.ToString());
        Assert.Equal(0.3, Convert.ToDouble(span.GetTagItem(GenAiAttributes.RequestTemperature)));
        Assert.Equal(128, Convert.ToInt32(span.GetTagItem(GenAiAttributes.RequestMaxTokens)));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldOpenNoSpan_WhenCalledInsideAChatSpan()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = StartRoot();
        var provider = TracedLlmProvider.Wrap(new MockLlmProvider { Name = "OpenAI" });

        using (var ambient = OrkeonActivitySources.Llm.StartActivity("chat ambient", ActivityKind.Client))
        {
            Assert.NotNull(ambient);
            ambient.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationChat);
            await provider.ChatAsync(Messages, Config(), Ct);
        }

        var span = Assert.Single(Chats(recorder, root.TraceId));
        Assert.Equal("chat ambient", span.DisplayName);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldOpenOneSpan_WhenProviderIsEnteredTwice()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = StartRoot();
        var once = TracedLlmProvider.Wrap(new MockLlmProvider { Name = "OpenAI" });
        var twice = TracedLlmProvider.Wrap(once);

        await twice.ChatAsync(Messages, Config(), Ct);

        Assert.Single(Chats(recorder, root.TraceId));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldSetErrorStatusAndType_WhenProviderThrows()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = StartRoot();
        var inner = new MockLlmProvider { Name = "OpenAI" };
        inner.SetChatFunc((_, _) => throw new InvalidOperationException("boom"));
        var provider = TracedLlmProvider.Wrap(inner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ChatAsync(Messages, Config(), Ct));

        var span = Assert.Single(Chats(recorder, root.TraceId));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem(GenAiAttributes.ErrorType)?.ToString());
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldSetErrorStatusAndType_WhenProviderReturnsAnErrorResponse()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = StartRoot();
        var inner = new MockLlmProvider { Name = "OpenAI" };
        inner.SetChatResult(new LlmResponse
        {
            Content = string.Empty,
            Metadata = new Dictionary<string, object>
            {
                [LlmResponseMetadataKeys.Error] = "The request timed out.",
                [LlmResponseMetadataKeys.ErrorType] = "TaskCanceledException",
            },
        });
        var provider = TracedLlmProvider.Wrap(inner);

        await provider.ChatAsync(Messages, Config(), Ct);

        var span = Assert.Single(Chats(recorder, root.TraceId));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("TaskCanceledException", span.GetTagItem(GenAiAttributes.ErrorType)?.ToString());
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldEndSpanWithStream_WhenProviderStreams()
    {
        using var recorder = new ActivityRecorder(OrkeonActivitySources.Llm.Name);
        using var root = StartRoot();
        var inner = new MockStreamingLlmProvider { Name = "OpenAI" };
        var provider = (IStreamingLlmProvider)TracedLlmProvider.Wrap(inner);

        var stream = provider.ChatStreamingAsync(Messages, Config(), Ct);
        var enumerator = stream.GetAsyncEnumerator(Ct);
        Assert.True(await enumerator.MoveNextAsync());
        var endedWhileOpen = Chats(recorder, root.TraceId).Count;
        while (await enumerator.MoveNextAsync())
        {
            // drain
        }

        await enumerator.DisposeAsync();
        Assert.Equal(0, endedWhileOpen);
        var span = Assert.Single(Chats(recorder, root.TraceId));
        Assert.Equal("chat m-1", span.DisplayName);
    }
}
