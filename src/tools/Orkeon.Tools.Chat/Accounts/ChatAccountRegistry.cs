using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Orkeon.Tools.Chat.Configuration;

namespace Orkeon.Tools.Chat.Accounts;

/// <summary>The declared accounts, resolved on first use.</summary>
internal interface IChatAccountRegistry
{
    /// <summary>Declared account names, sorted.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>The account a call without <c>account</c> uses, or null when that is ambiguous.</summary>
    string? DefaultName { get; }

    /// <summary>
    /// The account named <paramref name="name"/>, or the default one when null. Throws an
    /// <see cref="ChatToolException"/> that says what to fix when there is no such usable account.
    /// </summary>
    ResolvedChatAccount Resolve(string? name);

    /// <summary>The resolution of a declared account, problems included, without throwing.</summary>
    ChatAccountResolution Inspect(string name);
}

/// <summary>Default <see cref="IChatAccountRegistry"/> over the bound <c>Orkeon:Tools:Chat</c> section.</summary>
internal sealed class ChatAccountRegistry : IChatAccountRegistry
{
    private const string GuideHint = "see the chat guide, " + Constants.ChatDefaults.GuidePath;

    private readonly ChatToolsOptions _options;
    private readonly ConcurrentDictionary<string, ChatAccountResolution> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<string>> _names;

    /// <summary>Creates the registry over the bound options.</summary>
    public ChatAccountRegistry(IOptions<ChatToolsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        // The bound options are a snapshot: the names are sorted once, like the resolutions are cached.
        _names = new(() => _options.Accounts.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names => _names.Value;

    /// <inheritdoc />
    public string? DefaultName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_options.DefaultAccount))
                return _options.DefaultAccount.Trim();
            return _options.Accounts.Count == 1 ? _options.Accounts.Keys.First() : null;
        }
    }

    /// <inheritdoc />
    public ResolvedChatAccount Resolve(string? name)
    {
        if (_options.Accounts.Count == 0)
        {
            throw new ChatToolException(
                ChatErrorCode.NotConfigured,
                $"No chat account is configured. Declare one under {Constants.ChatDefaults.SectionName}:Accounts ({GuideHint}).");
        }

        var target = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        if (target is null)
        {
            throw new ChatToolException(
                ChatErrorCode.UnknownAccount,
                $"Several chat accounts are configured ({string.Join(", ", Names)}): pass `account` to choose one.");
        }

        var resolution = Inspect(target);
        if (resolution.Account is not null)
            return resolution.Account;

        throw new ChatToolException(
            ChatErrorCode.InvalidConfiguration,
            $"The chat account '{target}' is misconfigured: {string.Join("; ", resolution.Problems)} ({GuideHint}).");
    }

    /// <inheritdoc />
    public ChatAccountResolution Inspect(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = _options.Accounts.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            throw new ChatToolException(
                ChatErrorCode.UnknownAccount,
                $"Unknown chat account '{name}'. Configured: {(Names.Count == 0 ? "none" : string.Join(", ", Names))}.");
        }

        return _resolved.GetOrAdd(key, k => Unreadable(k) is { Count: > 0 } problems
            ? new ChatAccountResolution(null, problems)
            : ChatAccountResolver.Resolve(k, _options.Accounts[k]));
    }

    /// <summary>The settings of <paramref name="account"/>, and of the section, that could not even be read.</summary>
    private List<string> Unreadable(string account)
    {
        var problems = new List<string>(_options.SectionProblems);
        if (_options.AccountProblems.TryGetValue(account, out var own))
            problems.AddRange(own);
        return problems;
    }
}
