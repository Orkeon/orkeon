using Orkeon.Trading.Tools.Infrastructure.Base;
using Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;
using Microsoft.Extensions.Logging;

namespace Orkeon.Trading.Tools.Infrastructure.Governance;

public class AlertManagementTool(ILogger<AlertManagementTool>? logger = null)
    : TradingToolBase<AlertManagementRequest, AlertManagementResponse>(logger)
{
    protected override string ToolId => "alert_management";

    protected override async Task<AlertManagementResponse> ExecuteTypedAsync(
        AlertManagementRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var alertId = Guid.NewGuid().ToString();
        var channels = request.Severity == "CRITICAL"
            ? new List<string> { "email", "sms", "slack" }
            : new List<string> { "email" };

        _logger?.LogWarning("ALERT [{Severity}] {Type}: {Message}", request.Severity, request.AlertType, request.Message);

        return new AlertManagementResponse
        {
            AlertCreated = true,
            AlertId = alertId,
            NotificationSent = true,
            Channels = channels
        };
    }
}
