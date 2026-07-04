using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Dlp;

namespace Orkeon.Infrastructure.Tests.Security.Dlp;

public class DlpInterceptorTests
{
    private readonly IPiiDetector _detector = new PiiDetector();

    [Fact]
    public async Task ToolOutputInterceptor_BlockAction_ReplacesContent()
    {
        var interceptor = new ToolOutputDlpInterceptor(_detector);
        var policy = new DlpPolicy { Channel = DlpChannel.ToolOutput, DefaultAction = DlpAction.Block, Enabled = true };

        var result = await interceptor.InterceptAsync("SSN: 123-45-6789", policy, TestContext.Current.CancellationToken);

        Assert.Equal(DlpAction.Block, result.ActionTaken);
        Assert.Equal("[BLOCKED: PII detected in ToolOutput channel]", result.ProcessedContent);
    }

    [Fact]
    public async Task DelegationInterceptor_MaskAction_MasksPii()
    {
        var interceptor = new DelegationDlpInterceptor(_detector);
        var policy = new DlpPolicy { Channel = DlpChannel.Delegation, DefaultAction = DlpAction.Mask, Enabled = true };

        var result = await interceptor.InterceptAsync("Email: user@example.com", policy, TestContext.Current.CancellationToken);

        Assert.Equal(DlpAction.Mask, result.ActionTaken);
        Assert.DoesNotContain("user@example.com", result.ProcessedContent);
    }

    [Fact]
    public async Task LogInterceptor_AuditAction_PassesThrough()
    {
        var interceptor = new LogDlpInterceptor(_detector);
        var policy = new DlpPolicy { Channel = DlpChannel.Log, DefaultAction = DlpAction.Audit, Enabled = true };
        var content = "SSN: 123-45-6789";

        var result = await interceptor.InterceptAsync(content, policy, TestContext.Current.CancellationToken);

        Assert.Equal(DlpAction.Audit, result.ActionTaken);
        Assert.Equal(content, result.ProcessedContent);
        Assert.NotEmpty(result.DetectedPii);
    }

    [Fact]
    public async Task MemoryInterceptor_DetectsPii_InContent()
    {
        var interceptor = new MemoryDlpInterceptor(_detector);
        var policy = new DlpPolicy { Channel = DlpChannel.Memory, DefaultAction = DlpAction.Mask, Enabled = true };

        var result = await interceptor.InterceptAsync("Card: 4111-1111-1111-1111", policy, TestContext.Current.CancellationToken);

        Assert.Equal(DlpAction.Mask, result.ActionTaken);
        Assert.NotEmpty(result.DetectedPii);
        Assert.Contains(result.DetectedPii, m => m.Type == PiiType.CreditCard);
    }

    [Fact]
    public async Task ExternalOutputInterceptor_DisabledPolicy_Allows()
    {
        var interceptor = new ExternalOutputDlpInterceptor(_detector);
        var policy = new DlpPolicy { Channel = DlpChannel.ExternalOutput, Enabled = false };
        var content = "SSN: 123-45-6789";

        var result = await interceptor.InterceptAsync(content, policy, TestContext.Current.CancellationToken);

        Assert.Equal(DlpAction.Allow, result.ActionTaken);
        Assert.Equal(content, result.ProcessedContent);
    }

    [Fact]
    public async Task Interceptor_NoPii_AllowsContent()
    {
        var interceptor = new ToolOutputDlpInterceptor(_detector);
        var policy = new DlpPolicy { Channel = DlpChannel.ToolOutput, DefaultAction = DlpAction.Block, Enabled = true };
        var content = "This is safe content with no PII.";

        var result = await interceptor.InterceptAsync(content, policy, TestContext.Current.CancellationToken);

        Assert.Equal(DlpAction.Allow, result.ActionTaken);
        Assert.Equal(content, result.ProcessedContent);
    }
}
