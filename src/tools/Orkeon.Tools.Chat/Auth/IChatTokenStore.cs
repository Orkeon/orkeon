namespace Orkeon.Tools.Chat.Auth;

/// <summary>The OAuth tokens held for one chat account.</summary>
public sealed record ChatTokenSet
{
    /// <summary>The bearer token sent to Microsoft Graph.</summary>
    public required string AccessToken { get; init; }

    /// <summary>The long-lived token that obtains new access tokens, when the provider issued one.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>When <see cref="AccessToken"/> stops being accepted.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The scopes the tokens were granted for.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    // The generated formatting would print both tokens: a log line or an exception that holds this record must not.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
        Justification = "False positive: the compiler-generated ToString of the record calls PrintMembers.")]
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("AccessToken = ***, RefreshToken = ").Append(RefreshToken is null ? "null" : "***")
            .Append(", ExpiresAt = ").Append(ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}

/// <summary>
/// Where the OAuth tokens of chat accounts are kept between runs. The runner host provides
/// one over an internal VFS root that no agent-facing tool resolves; a host of its own may
/// supply any implementation, a vault-backed one for instance.
/// </summary>
public interface IChatTokenStore
{
    /// <summary>The tokens stored under <paramref name="key"/>, or null.</summary>
    Task<ChatTokenSet?> ReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="tokens"/> under <paramref name="key"/>, replacing what was there.</summary>
    Task WriteAsync(string key, ChatTokenSet tokens, CancellationToken cancellationToken);

    /// <summary>Forgets the tokens under <paramref name="key"/>; false when there were none.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets every set of tokens of <paramref name="account"/>, whatever settings they were issued
    /// under: the keys made of its name, a hyphen and exactly 12 hexadecimal digits, the name's case
    /// aside. <paramref name="keptKey"/>, when not null, stays. False when there were none to forget.
    /// </summary>
    Task<bool> DeleteAccountAsync(string account, string? keptKey, CancellationToken cancellationToken);
}
