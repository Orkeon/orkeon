using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Application.Tests.Services.Security;

/// <summary>CH-05: a dictionary sent as a key/value array reaches the tool as the dictionary.</summary>
public class ToolInvocationPipelineMapParameterTests
{
    [Fact]
    [Trait("BR", "CH-05")]
    public async System.Threading.Tasks.Task ShouldReadAKeyValueArrayAsTheDictionary_WhenTheCallGoesThroughTheInvocationPipeline()
    {
        using var tool = new TagsTool();
        var pipeline = new ToolInvocationPipeline(new FakeGuardianPipeline(), new FakeToolResultSanitizer(), new MockAuditLogger());
        using var document = JsonDocument.Parse("""[ { "key": "topic", "value": "weather" }, { "key": "lang", "value": "fr" } ]""");
        var arguments = new Dictionary<string, object?> { ["tags"] = document.RootElement.Clone() };

        var result = await pipeline.InvokeAsync(
            new ToolInvocation(tool, arguments, new ToolInvocationCaller("agent-1", "writer", "task-1", "crew-1")),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ConversationText);
        Assert.NotNull(tool.Received);
        Assert.Equal(2, tool.Received.Count);
        Assert.Equal("weather", tool.Received["topic"]);
        Assert.Equal("fr", tool.Received["lang"]);
    }

    public sealed record TagsRequest
    {
        [FieldSchema(Description = "tags")]
        public ImmutableDictionary<string, string>? Tags { get; init; }
    }

    public sealed record TagsResponse
    {
        public bool Ok { get; init; }
    }

    private sealed class TagsTool : ToolBase<TagsRequest, TagsResponse>
    {
        public ImmutableDictionary<string, string>? Received { get; private set; }

        public override string Name => "tags";
        public override string Description => "records its tags";

        protected override System.Threading.Tasks.Task<TagsResponse> ExecuteTypedAsync(
            TagsRequest request, CancellationToken cancellationToken)
        {
            Received = request.Tags;
            return System.Threading.Tasks.Task.FromResult(new TagsResponse { Ok = true });
        }
    }
}
