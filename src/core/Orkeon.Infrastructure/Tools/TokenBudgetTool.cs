using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Session;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="TokenBudgetTool"/> (no parameters).</summary>
public sealed class TokenBudgetRequest
{
}

/// <summary>Typed response for <see cref="TokenBudgetTool"/>.</summary>
public sealed class TokenBudgetResponse
{
    /// <summary>Model context window size in tokens.</summary>
    [JsonPropertyName("context_window_tokens")]
    public int ContextWindowTokens { get; set; }

    /// <summary>Estimated tokens used by the current buffer.</summary>
    [JsonPropertyName("used_tokens")]
    public int UsedTokens { get; set; }

    /// <summary>Remaining budget (window − used).</summary>
    [JsonPropertyName("available_tokens")]
    public int AvailableTokens { get; set; }

    /// <summary>Active model name.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>What fills <see cref="UsedTokens"/>, by kind of buffer entry; the kinds add up to it.</summary>
    [JsonPropertyName("used_tokens_by_kind")]
    public TokenBudgetByKind UsedTokensByKind { get; set; } = new();
}

/// <summary>
/// The tokens of the session buffer by kind of entry, estimated as <c>used_tokens</c> is (characters of the
/// role and the content, divided by 4).
/// </summary>
public sealed class TokenBudgetByKind
{
    /// <summary>System entries (<c>system</c>).</summary>
    [JsonPropertyName("system")]
    public int System { get; set; }

    /// <summary>The user's entries (<c>user</c>).</summary>
    [JsonPropertyName("user")]
    public int User { get; set; }

    /// <summary>The model's answers (<c>assistant</c>).</summary>
    [JsonPropertyName("assistant")]
    public int Assistant { get; set; }

    /// <summary>Tool results (<c>tool</c>).</summary>
    [JsonPropertyName("tool")]
    public int Tool { get; set; }

    /// <summary>Every other entry (a <c>note</c>, a role no kind above names): the rest of <c>used_tokens</c>.</summary>
    [JsonPropertyName("other")]
    public int Other { get; set; }
}

/// <summary>
/// Reports the model's context window, used tokens, and available budget for the current
/// session (backs SessionCompact + <c>/compact</c>).
/// </summary>
/// <remarks>
/// The domain <c>ILlmProvider</c> exposes no context-window API, so the window and model name come
/// from the settings: <c>Orkeon:Cli:Session:ContextWindowTokens</c> (<see cref="CliSessionOptions"/>,
/// default 200000, judged at the host's start) and <c>Llm:Model</c>. Used tokens come from the
/// buffer's heuristic, split by kind of entry in <see cref="TokenBudgetResponse.UsedTokensByKind"/>.
/// </remarks>
public sealed class TokenBudgetTool : ToolBase<TokenBudgetRequest, TokenBudgetResponse>
{
    /// <inheritdoc />
    public override string Name => "token_budget";
    /// <inheritdoc />
    public override string Description =>
        "Return the current model's context window size, used tokens (split by kind: system, user, assistant, tool, other), and available budget.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly ISessionBufferService _buffer;
    private readonly IConfiguration? _configuration;
    private readonly int _contextWindow;

    /// <summary>Creates the tool with its backing service(s).</summary>
    /// <param name="buffer">The session buffer whose tokens are counted.</param>
    /// <param name="configuration">The host configuration, for <c>Llm:Model</c>; null reports the model as unknown.</param>
    /// <param name="session">The session settings, for the context window; null budgets against the default.</param>
    public TokenBudgetTool(
        ISessionBufferService buffer,
        IConfiguration? configuration = null,
        IOptions<CliSessionOptions>? session = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _configuration = configuration;
        _contextWindow = session?.Value.ContextWindowTokens ?? CliSessionOptions.DefaultContextWindowTokens;
    }

    /// <inheritdoc />
    protected override Task<TokenBudgetResponse> ExecuteTypedAsync(
        TokenBudgetRequest request, CancellationToken cancellationToken)
    {
        var contextWindow = _contextWindow;
        var model = _configuration?["Llm:Model"] ?? "unknown";
        var used = _buffer.EstimateTokenCount();

        return Task.FromResult(new TokenBudgetResponse
        {
            ContextWindowTokens = contextWindow,
            UsedTokens = used,
            AvailableTokens = Math.Max(0, contextWindow - used),
            Model = model,
            UsedTokensByKind = ByKind(_buffer.GetMessages(), used),
        });
    }

    /// <summary>
    /// Splits <paramref name="used"/> by kind of entry with the buffer's heuristic (characters / 4); <c>other</c>
    /// takes what the named kinds leave, so the kinds add up to <paramref name="used"/>.
    /// </summary>
    private static TokenBudgetByKind ByKind(IReadOnlyList<SessionMessage> messages, int used)
    {
        long system = 0, user = 0, assistant = 0, tool = 0;
        foreach (var m in messages)
        {
            var chars = (long)(m.Role?.Length ?? 0) + (m.Content?.Length ?? 0);
            switch (m.Role)
            {
                case "system": system += chars; break;
                case "user": user += chars; break;
                case "assistant": assistant += chars; break;
                case "tool": tool += chars; break;
            }
        }

        var byKind = new TokenBudgetByKind
        {
            System = Tokens(system),
            User = Tokens(user),
            Assistant = Tokens(assistant),
            Tool = Tokens(tool),
        };
        byKind.Other = Math.Max(0, used - byKind.System - byKind.User - byKind.Assistant - byKind.Tool);
        return byKind;

        static int Tokens(long chars) => (int)Math.Min(int.MaxValue, chars / 4);
    }
}
