using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class AuditTrailTool(ILogger<AuditTrailTool>? logger = null)
    : TradingToolBase<AuditTrailRequest, AuditTrailResponse>(logger)
{
    protected override string ToolId => "audit_trail";

    protected override async Task<AuditTrailResponse> ExecuteTypedAsync(
        AuditTrailRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var auditId = Guid.NewGuid().ToString();

        _logger?.LogInformation("Audit: {Action} by {Actor}", request.Action, request.Actor);

        return new AuditTrailResponse
        {
            AuditEntryCreated = true,
            AuditId = auditId,
            Timestamp = DateTime.UtcNow
        };
    }
}
