using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

internal static class ToolAssertions
{
    /// <summary>Casts <see cref="ToolCallResponse.Result"/> to <see cref="Dictionary{TKey,TValue}"/> for assertion convenience.</summary>
    public static Dictionary<string, object?> ResultDict(this ToolCallResponse response)
    {
        Assert.NotNull(response.Result);
        return Assert.IsType<Dictionary<string, object?>>(response.Result);
    }
}
