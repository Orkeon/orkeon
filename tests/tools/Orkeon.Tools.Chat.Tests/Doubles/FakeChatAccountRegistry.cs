using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;

namespace Orkeon.Tools.Chat.Tests.Doubles;

/// <summary>A registry of one account, already resolved.</summary>
internal sealed class FakeChatAccountRegistry(ResolvedChatAccount account) : IChatAccountRegistry
{
    /// <inheritdoc />
    public IReadOnlyList<string> Names => [account.Name];

    /// <inheritdoc />
    public string? DefaultName => account.Name;

    /// <inheritdoc />
    public ResolvedChatAccount Resolve(string? name) => account;

    /// <inheritdoc />
    public ChatAccountResolution Inspect(string name) => new(account, []);
}
