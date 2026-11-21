using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - the settings of both sides, judged at start.</summary>
public sealed class ToolBridgeOptionsTests
{
    [Theory]
    [InlineData("http://192.168.1.20:7410/", "requires https")]
    [InlineData("https://bridge.example:7410", "must end with '/'")]
    [InlineData("bridge:7410", "not an absolute http or https address")]
    public void ShouldRefuseThePrefix_WhenItIsNotAnAcceptableAddress(string prefix, string expected)
    {
        // Arrange
        var options = new ToolBridgeServerOptions { Prefix = prefix };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains(expected, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7410/")]
    [InlineData("http://localhost:7410/")]
    [InlineData("https://bridge.example:7410/")]
    [InlineData("http://host.docker.internal:7410/")]
    public void ShouldAcceptTheAddress_WhenItIsLoopbackOrHttpsOrDockerDesktopsHost(string address)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = address };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void ShouldBridgeNothing_WhenAllowIsEmpty()
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = "http://127.0.0.1:7410/" };

        // Act
        var enabled = options.IsEnabled;

        // Assert
        Assert.False(enabled);
    }

    [Theory]
    [InlineData("http://bridge.lan:7410/")]
    [InlineData("http://192.168.65.254:7410/")]
    [InlineData("http://host.docker.internal.example:7410/")]
    public void ShouldRequireHttps_WhenTheCallersAddressNamesAnotherHostOverHttp(string address)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = address };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains("requires https", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseDockerDesktopsHost_WhenThePrefixOfTheBridgeNamesIt()
    {
        // Arrange
        var options = new ToolBridgeServerOptions { Prefix = "http://host.docker.internal:7410/" };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains("requires https", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://host.docker.internal:7411/", null, "127.0.0.1:7411")]
    [InlineData("http://HOST.DOCKER.INTERNAL:7411/", null, "127.0.0.1:7411")]
    [InlineData("http://host.docker.internal:7411/", "localhost:7411", "localhost:7411")]
    [InlineData("http://127.0.0.1:7411/", null, null)]
    [InlineData("https://bridge.example:7410/", null, null)]
    public void ShouldSendTheHostHeaderOfThePrefix_WhenTheAddressNamesDockerDesktopsHost(string address, string? set, string? expected)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = address, HostHeader = set };

        // Act
        var header = options.EffectiveHostHeader();

        // Assert
        Assert.Equal(expected, header);
    }

    [Theory]
    [InlineData("127.0.0.1:7410/x")]
    [InlineData("user@127.0.0.1:7410")]
    [InlineData("127.0.0.1 7410")]
    public void ShouldRefuseTheHostHeader_WhenItIsNotAHostWithAPort(string header)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = "http://host.docker.internal:7410/", HostHeader = header };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains("HostHeader", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3_000_000_000L)]
    public void ShouldRefuseTheResponseLimit_WhenItIsOutOfBounds(long limit)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { MaxResponseBytes = limit };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains("MaxResponseBytes", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(700_000)]
    public void ShouldRefuseTheHeartbeatWindow_WhenItIsOutOfBounds(int milliseconds)
    {
        // Arrange
        var options = new ToolBridgeServerOptions { HeartbeatWindow = TimeSpan.FromMilliseconds(milliseconds) };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains("HeartbeatWindow", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ToolBridgeServerOptions.MaxSessions), 0)]
    [InlineData(nameof(ToolBridgeServerOptions.MaxSessions), 1025)]
    [InlineData(nameof(ToolBridgeServerOptions.MaxConcurrentCalls), 0)]
    [InlineData(nameof(ToolBridgeServerOptions.MaxCallsPerSession), 2000)]
    [InlineData(nameof(ToolBridgeServerOptions.MaxRunsPerSession), -1)]
    public void ShouldRefuseACountLimit_WhenItIsOutOfBounds(string key, int value)
    {
        // Arrange
        var options = new ToolBridgeServerOptions();
        typeof(ToolBridgeServerOptions).GetProperty(key)!.SetValue(options, value);

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains(key, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ToolBridgeServerOptions.RequestReadTimeout), 500)]
    [InlineData(nameof(ToolBridgeServerOptions.RequestReadTimeout), 700_000)]
    [InlineData(nameof(ToolBridgeServerOptions.StopTimeout), -1)]
    [InlineData(nameof(ToolBridgeServerOptions.StopTimeout), 700_000)]
    public void ShouldRefuseATimeout_WhenItIsOutOfBounds(string key, int milliseconds)
    {
        // Arrange
        var options = new ToolBridgeServerOptions();
        typeof(ToolBridgeServerOptions).GetProperty(key)!.SetValue(options, TimeSpan.FromMilliseconds(milliseconds));

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains(key, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseTheAddressWithoutEchoingIt_WhenItNamesAUserAndAPassword()
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = "https://someone:hunter2@bridge.example:7410/" };

        // Act
        var problem = Assert.Single(options.Problems().ToList());

        // Assert
        Assert.Contains("user and a password", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://loopback:7410/")]
    [InlineData("http://localhost.example:7410/")]
    public void ShouldRequireHttps_WhenTheHostIsANameThatIsNotLocalhost(string address)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = address };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Contains("requires https", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://[::1]:7410/")]
    [InlineData("http://127.0.0.2:7410/")]
    public void ShouldAcceptTheAddress_WhenItIsALoopbackLiteral(string address)
    {
        // Arrange
        var options = new ToolBridgeClientOptions { Address = address };

        // Act
        var problems = options.Problems().ToList();

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    public void ShouldDefaultThePrefixToTheLoopbackOfThePlatform_WhenNoneIsSet()
    {
        // Arrange
        var options = new ToolBridgeServerOptions();

        // Act
        var prefix = options.Prefix;

        // Assert
        Assert.Equal(OperatingSystem.IsWindows() ? "http://localhost:7410/" : "http://127.0.0.1:7410/", prefix);
    }
}
