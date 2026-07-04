using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Dlp;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Security.Dlp;

public class DlpPolicyProviderTests
{
    [Fact]
    public void GetPolicy_ReturnsChannelPolicy()
    {
        var channelPolicy = new DlpPolicy
        {
            Channel = DlpChannel.ToolOutput,
            DefaultAction = DlpAction.Block,
            Enabled = true
        };
        var options = Options.Create(new DlpOptions
        {
            ChannelPolicies = new Dictionary<DlpChannel, DlpPolicy>
            {
                [DlpChannel.ToolOutput] = channelPolicy
            }
        });
        var provider = new DlpPolicyProvider(options);

        var result = provider.GetPolicy(DlpChannel.ToolOutput);

        Assert.Equal(DlpAction.Block, result.DefaultAction);
        Assert.Equal(DlpChannel.ToolOutput, result.Channel);
    }

    [Fact]
    public void GetPolicy_DefaultsWhenNotConfigured()
    {
        var options = Options.Create(new DlpOptions
        {
            DefaultAction = DlpAction.Audit,
            Enabled = true
        });
        var provider = new DlpPolicyProvider(options);

        var result = provider.GetPolicy(DlpChannel.Log);

        Assert.Equal(DlpAction.Audit, result.DefaultAction);
        Assert.True(result.Enabled);
        Assert.Equal(DlpChannel.Log, result.Channel);
    }
}
