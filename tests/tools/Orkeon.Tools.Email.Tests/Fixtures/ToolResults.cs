using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.Email.Tests.Fixtures;

/// <summary>Reads the dictionaries a tool call returns.</summary>
internal static class ToolResults
{
    /// <summary>The result dictionary of a successful call.</summary>
    public static Dictionary<string, object?> Success(ToolCallResponse response)
    {
        Assert.True(response.Success, "The call should succeed: " + response.Error);
        return Assert.IsType<Dictionary<string, object?>>(response.Result);
    }

    /// <summary>The error of a failed call.</summary>
    public static string Failure(ToolCallResponse response)
    {
        Assert.False(response.Success, "The call should fail.");
        Assert.NotNull(response.Error);
        return response.Error;
    }

    /// <summary>A nested object (the serializer materializes JSON objects as dictionaries, arrays as lists).</summary>
    public static Dictionary<string, object?> Object(Dictionary<string, object?> result, string key) =>
        Assert.IsType<Dictionary<string, object>>(result[key])!;

    /// <summary>A nested array of objects.</summary>
    public static List<Dictionary<string, object?>> Objects(Dictionary<string, object?> result, string key) =>
        Assert.IsType<List<object>>(result[key]).Select(item => (Dictionary<string, object?>)Assert.IsType<Dictionary<string, object>>(item)!).ToList();

    /// <summary>A nested array of strings.</summary>
    public static List<string> Strings(Dictionary<string, object?> result, string key) =>
        Assert.IsType<List<object>>(result[key]).Select(item => Assert.IsType<string>(item)).ToList();

    /// <summary>A list argument, as an agent passes one.</summary>
    public static List<string> Of(params string[] values) => [.. values];

    /// <summary>A tool call.</summary>
    public static ToolCallRequest Call(string tool, params (string Key, object? Value)[] parameters) =>
        new(tool, parameters.ToDictionary(p => p.Key, p => p.Value));
}
