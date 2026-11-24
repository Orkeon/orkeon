namespace Orkeon.Tools.Chat.Auth;

/// <summary>
/// The token store of a host that provides none. A chat account gets a message that says
/// where tokens come from instead of a silent failure.
/// </summary>
internal sealed class UnavailableChatTokenStore : IChatTokenStore
{
    private const string Message =
        "This host keeps no OAuth tokens for chat accounts. Use the runner of the orkeon CLI, which keeps them once a " +
        "chat account is declared and its token directory can be created (it warns at start when it cannot); a host " +
        "of its own registers an IChatTokenStore.";

    /// <inheritdoc />
    public Task<ChatTokenSet?> ReadAsync(string key, CancellationToken cancellationToken) =>
        throw new ChatToolException(ChatErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task WriteAsync(string key, ChatTokenSet tokens, CancellationToken cancellationToken) =>
        throw new ChatToolException(ChatErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        throw new ChatToolException(ChatErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task<bool> DeleteAccountAsync(string account, string? keptKey, CancellationToken cancellationToken) =>
        throw new ChatToolException(ChatErrorCode.NotConfigured, Message);
}
