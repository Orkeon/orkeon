using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Administration;
using Orkeon.Tools.Email.Dtos;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Lists the configured e-mail accounts, their rights and whether each is ready.</summary>
[ToolContract("email_accounts",
    Name = "email_accounts",
    Description = "List the e-mail accounts configured for this run: the name to pass as `account`, what each may do (rights: Read, Organize, Draft, Send, Delete, Purge) and whether it is ready.",
    Category = "Email")]
internal sealed class EmailAccountsTool : ToolBase<EmailAccountsRequest, EmailAccountsResponse>
{
    private readonly EmailAccountAdministration _administration;

    /// <summary>Creates the tool.</summary>
    public EmailAccountsTool(EmailAccountAdministration administration, ILogger<EmailAccountsTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(administration);
        _administration = administration;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<EmailAccountsResponse> ExecuteTypedAsync(EmailAccountsRequest request, CancellationToken cancellationToken)
    {
        var statuses = await _administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return new EmailAccountsResponse
        {
            Accounts = statuses.Select(status => new EmailAccountDto
            {
                Name = status.Name,
                Address = status.Address,
                Provider = status.Provider,
                Reads = status.Reads,
                Sends = status.Sends,
                Rights = status.Rights,
                IsDefault = status.IsDefault,
                Ready = status.Ready,
                Problem = status.Problem,
            }).ToList(),
            DefaultAccount = statuses.FirstOrDefault(status => status.IsDefault)?.Name,
        };
    }
}
