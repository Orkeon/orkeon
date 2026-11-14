using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>The first turn of each act asks for one <c>file_write</c> call; the turn after it answers <c>final</c>.</summary>
internal sealed class FakeWriteOnceLlmProvider : ILlmProvider
{
    public string Name => "fake-write-once";
    public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse { Content = prompt });

    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        if (messages.Any(m => m.Role == "tool") || config?.Tools is null)
            return Task.FromResult(new LlmResponse { Content = "final" });
        return Task.FromResult(new LlmResponse
        {
            Content = "",
            RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"file_write\",\"arguments\":\"{}\"}}]}}]}",
        });
    }
}
