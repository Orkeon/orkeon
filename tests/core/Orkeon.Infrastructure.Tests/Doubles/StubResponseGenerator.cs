using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Minimal hand-written stub of <see cref="IResponseGenerator"/>: returns a fixed answer.
/// Used to run a real <see cref="RagPipeline"/> in tests without an LLM.
/// </summary>
public sealed class StubResponseGenerator : IResponseGenerator
{
    public System.Threading.Tasks.Task<GeneratedResponse> GenerateAsync(
        AugmentedPrompt prompt,
        GenerationOptions options,
        CancellationToken ct = default)
    {
        return System.Threading.Tasks.Task.FromResult(new GeneratedResponse
        {
            Text = "stub answer",
            TokensUsed = 0,
        });
    }
}
