using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Testing;

/// <summary>
/// In-memory <see cref="ILlmProvider"/> for tests. Calls are recorded and matched
/// against a list of expectations registered through the <c>test.mockLlm</c> binding.
/// </summary>
public sealed class MockLlmProvider : ILlmProvider
{
    private readonly List<MockLlmExpectation> _expectations = new();
    private readonly List<string> _generateCalls = new();
    private readonly List<LlmMessage[]> _chatCalls = new();

    /// <inheritdoc />
    public string Name => "mock";

    /// <summary>Captured prompts from <see cref="GenerateAsync"/>.</summary>
    public IReadOnlyList<string> GenerateCalls => _generateCalls;

    /// <summary>Captured message sets from <see cref="ChatAsync"/>.</summary>
    public IReadOnlyList<LlmMessage[]> ChatCalls => _chatCalls;

    /// <summary>Number of times the mock was invoked across all entry points.</summary>
    public int TotalCalls => _generateCalls.Count + _chatCalls.Count;

    internal void AddExpectation(MockLlmExpectation expectation) => _expectations.Add(expectation);

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        _generateCalls.Add(prompt);
        var match = _expectations.FirstOrDefault(e => e.Matches(prompt));
        return Task.FromResult(new LlmResponse { Content = match?.ResponseContent ?? string.Empty });
    }

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        _chatCalls.Add(messages);
        var lastUser = messages.LastOrDefault(m => m.Role == "user");
        var match = lastUser is null
            ? null
            : _expectations.FirstOrDefault(e => e.Matches(lastUser.Content));
        return Task.FromResult(new LlmResponse { Content = match?.ResponseContent ?? string.Empty });
    }
}

/// <summary>Single (matcher → response) entry registered on a <see cref="MockLlmProvider"/>.</summary>
internal sealed class MockLlmExpectation
{
    public string? PromptContains { get; }
    public Regex? PromptRegex { get; }
    public string ResponseContent { get; }

    public MockLlmExpectation(string? contains, Regex? regex, string response)
    {
        PromptContains = contains;
        PromptRegex = regex;
        ResponseContent = response;
    }

    public bool Matches(string prompt)
    {
        if (PromptRegex is not null) return PromptRegex.IsMatch(prompt);
        if (PromptContains is not null)
            return prompt.Contains(PromptContains, StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
