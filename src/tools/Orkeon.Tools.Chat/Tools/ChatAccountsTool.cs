using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Administration;
using Orkeon.Tools.Chat.Dtos;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Lists the configured chat accounts, their rights and whether each is ready.</summary>
[ToolContract("chat_accounts",
    Name = "chat_accounts",
    Description = "List the chat accounts (Microsoft Teams) configured for this run: the name to pass as `account`, what each may do (rights: Read, Send, Edit, Delete, React, Meetings) and whether it is ready.",
    Category = "Chat")]
internal sealed class ChatAccountsTool : ToolBase<ChatAccountsRequest, ChatAccountsResponse>
{
    private readonly ChatAccountAdministration _administration;

    /// <summary>Creates the tool.</summary>
    public ChatAccountsTool(ChatAccountAdministration administration, ILogger<ChatAccountsTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(administration);
        _administration = administration;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<ChatAccountsResponse> ExecuteTypedAsync(ChatAccountsRequest request, CancellationToken cancellationToken)
    {
        var statuses = await _administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return new ChatAccountsResponse
        {
            Accounts = statuses.Select(status => new ChatAccountDto
            {
                Name = status.Name,
                Provider = status.Provider,
                Rights = status.Rights,
                Default = status.IsDefault,
                Ready = status.Ready,
                Problem = status.Problem,
            }).ToList(),
        };
    }
}
