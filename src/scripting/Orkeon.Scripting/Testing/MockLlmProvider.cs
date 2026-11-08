using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Testing;

/// <summary>
/// In-memory <see cref="ILlmProvider"/> for tests. Calls are recorded and matched
/// against a list of expectations registered through the <c>test.mockLlm</c> binding,
/// or from .NET through <see cref="When(string)"/> / <see cref="When(Regex)"/>.
/// Safe for concurrent calls: the crews of <c>orkeon test</c> run agents in parallel, and the
/// recorded calls are read back as snapshots.
/// </summary>
public sealed class MockLlmProvider : ILlmProvider
{
    private readonly List<MockLlmExpectation> _expectations = new();
    private readonly List<string> _generateCalls = new();
    private readonly List<LlmMessage[]> _chatCalls = new();
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public string Name => "mock";

    /// <summary>Captured prompts from <see cref="GenerateAsync"/>: a snapshot, taken when read.</summary>
    public IReadOnlyList<string> GenerateCalls
    {
        get
        {
            lock (_gate)
                return _generateCalls.ToArray();
        }
    }

    /// <summary>Captured message sets from <see cref="ChatAsync"/>: a snapshot, taken when read.</summary>
    public IReadOnlyList<LlmMessage[]> ChatCalls
    {
        get
        {
            lock (_gate)
                return _chatCalls.ToArray();
        }
    }

    /// <summary>Number of times the mock was invoked across all entry points.</summary>
    public int TotalCalls
    {
        get
        {
            lock (_gate)
                return _generateCalls.Count + _chatCalls.Count;
        }
    }

    internal void AddExpectation(MockLlmExpectation expectation)
    {
        lock (_gate)
            _expectations.Add(expectation);
    }

    /// <summary>
    /// Starts an expectation for prompts that contain <paramref name="contains"/> (case ignored).
    /// Complete it with <see cref="MockLlmExpectationBuilder.Respond(string)"/>; the first
    /// expectation that fits a prompt answers it.
    /// </summary>
    /// <param name="contains">The text to look for in the prompt (the last user message of a chat).</param>
    public MockLlmExpectationBuilder When(string contains)
    {
        ArgumentNullException.ThrowIfNull(contains);
        return new MockLlmExpectationBuilder(this, contains, null);
    }

    /// <summary>
    /// Starts an expectation for prompts <paramref name="pattern"/> matches.
    /// Complete it with <see cref="MockLlmExpectationBuilder.Respond(string)"/>.
    /// </summary>
    /// <param name="pattern">The pattern the prompt (the last user message of a chat) is tested against.</param>
    public MockLlmExpectationBuilder When(Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new MockLlmExpectationBuilder(this, null, pattern);
    }

    /// <summary>The answer an expectation gives for <paramref name="prompt"/>, or null when none fits.</summary>
    internal LlmResponse? Match(string prompt)
    {
        MockLlmExpectation? match;
        lock (_gate)
            match = _expectations.FirstOrDefault(e => e.Matches(prompt));
        return match is null ? null : new LlmResponse { Content = match.ResponseContent };
    }

    internal void RecordGenerate(string prompt)
    {
        lock (_gate)
            _generateCalls.Add(prompt);
    }

    internal void RecordChat(LlmMessage[] messages)
    {
        lock (_gate)
            _chatCalls.Add(messages);
    }

    /// <summary>The prompt a chat is matched on: its last user message, or null when it has none.</summary>
    internal static string? PromptOf(LlmMessage[] messages) =>
        messages.LastOrDefault(m => m.Role == "user")?.Content;

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        RecordGenerate(prompt);
        return Task.FromResult(Match(prompt) ?? new LlmResponse { Content = string.Empty });
    }

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        RecordChat(messages);
        var lastUser = PromptOf(messages);
        var match = lastUser is null ? null : Match(lastUser);
        return Task.FromResult(match ?? new LlmResponse { Content = string.Empty });
    }
}

/// <summary>
/// Opens an expectation on a <see cref="MockLlmProvider"/> (<see cref="MockLlmProvider.When(string)"/>).
/// </summary>
public sealed class MockLlmExpectationBuilder
{
    private readonly MockLlmProvider _provider;
    private readonly string? _contains;
    private readonly Regex? _pattern;

    internal MockLlmExpectationBuilder(MockLlmProvider provider, string? contains, Regex? pattern)
    {
        _provider = provider;
        _contains = contains;
        _pattern = pattern;
    }

    /// <summary>Registers <paramref name="content"/> as the answer to the matched prompts.</summary>
    /// <param name="content">The text the provider answers with.</param>
    /// <returns>The provider, to chain another <c>When</c>.</returns>
    public MockLlmProvider Respond(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _provider.AddExpectation(new MockLlmExpectation(_contains, _pattern, content));
        return _provider;
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
