using Microsoft.Extensions.Options;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.CostTracking;

/// <summary>
/// Improved token counter with configurable character-to-token ratio.
/// Uses ~3.5 characters per token as default approximation (closer to BPE tokenizers).
/// Also provides message-level overhead estimation for chat-format requests.
/// </summary>
public sealed class EnhancedTokenCounter : ITokenCounter
{
    private readonly float _charsPerToken;
    private readonly int _tokensPerMessage;
    private readonly int _tokensPerReply;
    private readonly int _specialTokenOverhead;

    /// <summary>Initializes a new instance of <see cref="EnhancedTokenCounter"/>.</summary>
    /// <param name="options">The token counter configuration options.</param>
    public EnhancedTokenCounter(IOptions<TokenCounterOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        _charsPerToken = opts.CharsPerToken > 0 ? opts.CharsPerToken : 3.5f;
        _tokensPerMessage = opts.TokensPerMessage;
        _tokensPerReply = opts.TokensPerReply;
        _specialTokenOverhead = opts.SpecialTokenOverhead;
    }

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / _charsPerToken);
    }

    /// <summary>
    /// Counts the approximate tokens for a list of chat messages,
    /// including per-message overhead tokens used by chat-format models.
    /// </summary>
    /// <param name="messages">Sequence of (role, content) tuples.</param>
    /// <returns>Estimated token count including overhead.</returns>
    public int CountTokensForMessages(IEnumerable<(string role, string content)> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var total = 0;

        foreach (var (role, content) in messages)
        {
            total += _tokensPerMessage; // per-message overhead
            total += CountTokens(role);
            total += CountTokens(content);
        }

        // Add reply priming and special token overhead
        total += _tokensPerReply;
        total += _specialTokenOverhead;

        return total;
    }
}
