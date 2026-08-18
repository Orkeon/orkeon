using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.MCP;

public class JsonRpcTypesTests
{
    private readonly JsonRpcTypesTestsFixture _fixture = new();
    [Fact]
    public void ShouldSerializeAndDeserialize_WhenJsonRpcRequestRoundTripped()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Method = "tools/list",
            Id = JsonSerializer.SerializeToElement(42),
            Params = JsonDocument.Parse("{\"cursor\":\"abc\"}").RootElement
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("2.0", deserialized!.Jsonrpc);
        Assert.Equal("tools/list", deserialized.Method);
        Assert.Equal(42, deserialized.Id!.Value.GetInt32());
        Assert.NotNull(deserialized.Params);
        Assert.Equal("abc", deserialized.Params!.Value.GetProperty("cursor").GetString());
    }

    [Fact]
    public void ShouldSerializeCorrectly_WhenResponseHasResult()
    {
        // Arrange
        var response = new JsonRpcResponse
        {
            Id = JsonSerializer.SerializeToElement(1),
            Result = JsonDocument.Parse("{\"tools\":[]}").RootElement
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("2.0", deserialized!.Jsonrpc);
        Assert.Equal(1, deserialized.Id!.Value.GetInt32());
        Assert.NotNull(deserialized.Result);
        Assert.Null(deserialized.Error);
    }

    [Fact]
    public void ShouldSerializeCorrectly_WhenResponseHasError()
    {
        // Arrange
        var response = new JsonRpcResponse
        {
            Id = JsonSerializer.SerializeToElement(2),
            Error = new JsonRpcError(-32601, "Method not found")
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.Error);
        Assert.Equal(-32601, deserialized.Error!.Code);
        Assert.Equal("Method not found", deserialized.Error.Message);
        Assert.Null(deserialized.Result);
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenJsonRpcErrorConstructed()
    {
        // Act
        var error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "Method not found");

        // Assert
        Assert.Equal(-32601, error.Code);
        Assert.Equal("Method not found", error.Message);
        Assert.Null(error.Data);
    }

    [Fact]
    public void ShouldHaveNoId_WhenSerializingNotification()
    {
        // Arrange
        var notification = new JsonRpcNotification
        {
            Method = "notifications/initialized"
        };

        // Act
        var json = JsonSerializer.Serialize(notification);

        // Assert
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.Contains("\"method\":\"notifications/initialized\"", json);
        Assert.DoesNotContain("\"id\"", json);
    }

    [Fact]
    public void ShouldSerializeCorrectly_WhenParamsAreNull()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Method = "tools/list",
            Id = JsonSerializer.SerializeToElement(1)
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<JsonRpcRequest>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.Params);
    }

    [Fact]
    public void ShouldHaveCorrectValues_WhenAccessingErrorCodes()
    {
        Assert.Equal(-32700, JsonRpcErrorCodes.ParseError);
        Assert.Equal(-32600, JsonRpcErrorCodes.InvalidRequest);
        Assert.Equal(-32601, JsonRpcErrorCodes.MethodNotFound);
        Assert.Equal(-32602, JsonRpcErrorCodes.InvalidParams);
        Assert.Equal(-32603, JsonRpcErrorCodes.InternalError);
    }
}
