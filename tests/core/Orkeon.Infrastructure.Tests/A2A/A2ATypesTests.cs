using System.Text.Json;
using Orkeon.Infrastructure.AgentCommunication;

namespace Orkeon.Infrastructure.Tests.A2A;

public class A2ATypesTests
{
    [Fact]
    public void A2AMessage_ShouldSerializeWithJsonPropertyNames()
    {
        // Arrange
        var message = new A2AMessage
        {
            Method = "tasks/send",
            Id = "msg-1"
        };

        // Act
        var json = JsonSerializer.Serialize(message);

        // Assert
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"method\":\"tasks/send\"", json);
        Assert.Contains("\"id\":\"msg-1\"", json);
    }

    [Fact]
    public void A2AMessage_ShouldDeserializeResponse()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "resp-1",
            "result": { "taskId": "task-1", "status": "Completed" }
        }
        """;

        // Act
        var message = JsonSerializer.Deserialize<A2AMessage>(json);

        // Assert
        Assert.NotNull(message);
        Assert.Equal("2.0", message!.Jsonrpc);
        Assert.Equal("resp-1", message.Id);
        Assert.NotNull(message.Result);
        Assert.Null(message.Error);
    }

    [Fact]
    public void A2AMessage_ShouldDeserializeError()
    {
        // Arrange
        var json = """
        {
            "jsonrpc": "2.0",
            "id": "err-1",
            "error": { "code": -32602, "message": "Invalid params" }
        }
        """;

        // Act
        var message = JsonSerializer.Deserialize<A2AMessage>(json);

        // Assert
        Assert.NotNull(message);
        Assert.NotNull(message!.Error);
        Assert.Equal(-32602, message.Error!.Code);
        Assert.Equal("Invalid params", message.Error.Message);
    }

    [Fact]
    public void A2AError_ShouldCreateWithCodeAndMessage()
    {
        // Arrange & Act
        var error = new A2AError(A2AErrorCodes.SkillNotFound, "Skill not found: research");

        // Assert
        Assert.Equal(-32002, error.Code);
        Assert.Equal("Skill not found: research", error.Message);
        Assert.Null(error.Data);
    }

    [Fact]
    public void A2AErrorCodes_ShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal(-32700, A2AErrorCodes.ParseError);
        Assert.Equal(-32600, A2AErrorCodes.InvalidRequest);
        Assert.Equal(-32601, A2AErrorCodes.MethodNotFound);
        Assert.Equal(-32602, A2AErrorCodes.InvalidParams);
        Assert.Equal(-32603, A2AErrorCodes.InternalError);
        Assert.Equal(-32001, A2AErrorCodes.TaskNotFound);
        Assert.Equal(-32002, A2AErrorCodes.SkillNotFound);
    }
}
