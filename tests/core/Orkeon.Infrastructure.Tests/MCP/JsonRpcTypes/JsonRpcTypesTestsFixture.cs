using System.Text.Json;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Infrastructure.Tests.MCP;

public class JsonRpcTypesTestsFixture
{
    // --- Request factories ---

    public static JsonRpcRequest CreateRequest(string method = "tools/list", int id = 1, string? paramsJson = null)
    {
        var request = new JsonRpcRequest
        {
            Method = method,
            Id = id
        };
        if (paramsJson != null)
            request.Params = JsonDocument.Parse(paramsJson).RootElement;
        return request;
    }

    // --- Response factories ---

    public static JsonRpcResponse CreateResponseWithResult(int id, string resultJson)
        => new()
        {
            Id = id,
            Result = JsonDocument.Parse(resultJson).RootElement
        };

    public static JsonRpcResponse CreateResponseWithError(int id, int code, string message)
        => new()
        {
            Id = id,
            Error = new JsonRpcError(code, message)
        };

    // --- Notification factories ---

    public static JsonRpcNotification CreateNotification(string method)
        => new() { Method = method };

    // --- Serialization helpers ---

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value);

    public static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json);
}
