using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#434: a conversation view finds the call a tool result answers - its tool, its input, whether it only read.</summary>
public sealed class ConversationViewToolCallTests
{
    private static bool ReadsOnly(string name, IReadOnlyDictionary<string, object?> arguments) => name == "file_read";

    private static List<ChatMessage> Chat(string tool) =>
    [
        new(ChatRole.System, "sys"),
        new(ChatRole.User, "task"),
        new(ChatRole.Assistant, [new FunctionCallContent("call-1", tool, new Dictionary<string, object?> { ["path"] = "a.cs" })]),
        new(ChatRole.Tool, [new FunctionResultContent("call-1", "content")]),
    ];

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData("file_read", true)]
    [InlineData("file_write", false)]
    public void ShouldFindTheCallBeforeTheResult_WhenTheChatLoopsResultCarriesItsId(string tool, bool isRead)
    {
        // Arrange
        var view = new ChatConversationView(Chat(tool), ReadsOnly);

        // Act
        var call = view.ToolCallAt(3);

        // Assert
        Assert.Equal(new ConversationToolCall { CallId = "call-1", ToolName = tool, Input = "{\"path\":\"a.cs\"}", IsRead = isRead }, call);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData(2)]
    [InlineData(1)]
    public void ShouldFindNoCall_WhenTheChatMessageIsNotAToolResult(int index)
    {
        // Arrange
        var view = new ChatConversationView(Chat("file_read"), ReadsOnly);

        // Act
        var call = view.ToolCallAt(index);

        // Assert
        Assert.Null(call);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldFindTheCallTheNativeLoopNoted_WhenAToolResultCarriesItsId()
    {
        // Arrange
        var view = new LlmConversationView(
            [new LlmMessage { Role = "user", Content = "task" }, new LlmMessage { Role = "tool", Content = "content", ToolCallId = "call-1" }],
            ReadsOnly);
        view.NoteToolCall("call-1", "file_read", new Dictionary<string, object?> { ["path"] = "a.cs" });

        // Act
        var call = view.ToolCallAt(1);

        // Assert
        Assert.Equal(new ConversationToolCall { CallId = "call-1", ToolName = "file_read", Input = "{\"path\":\"a.cs\"}", IsRead = true }, call);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldFindNoCall_WhenTheNativeLoopNotedNoneForTheResult()
    {
        // Arrange
        var view = new LlmConversationView([new LlmMessage { Role = "tool", Content = "content", ToolCallId = "call-9" }], ReadsOnly);

        // Act
        var call = view.ToolCallAt(0);

        // Assert
        Assert.Null(call);
    }
}
