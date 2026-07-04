using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IResponseGenerator for testing.
/// </summary>
public sealed class MockResponseGenerator : IResponseGenerator
{
    private GeneratedResponse _generateResult = new()
    {
        Text = "mock generated response",
        TokensUsed = 100,
        Model = "mock-model"
    };

    private Func<AugmentedPrompt, GenerationOptions, GeneratedResponse>? _generateFunc;

    // --- Tracking ---
    public int GenerateCallCount { get; private set; }
    public AugmentedPrompt? LastGeneratePrompt { get; private set; }
    public GenerationOptions? LastGenerateOptions { get; private set; }

    // --- Configuration ---
    public void SetGenerateResult(GeneratedResponse result) => _generateResult = result;
    public void SetGenerateResult(string text) =>
        _generateResult = new GeneratedResponse { Text = text, TokensUsed = 0 };
    public void SetGenerateFunc(Func<AugmentedPrompt, GenerationOptions, GeneratedResponse> func) =>
        _generateFunc = func;

    public System.Threading.Tasks.Task<GeneratedResponse> GenerateAsync(
        AugmentedPrompt prompt,
        GenerationOptions options,
        CancellationToken ct = default)
    {
        GenerateCallCount++;
        LastGeneratePrompt = prompt;
        LastGenerateOptions = options;

        var result = _generateFunc != null ? _generateFunc(prompt, options) : _generateResult;
        return System.Threading.Tasks.Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GenerateCallCount = 0;
        LastGeneratePrompt = null;
        LastGenerateOptions = null;
    }
}
