using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// The variables a shell session starts with (#216): the allow-list, the sockets of the operator's
/// session, and nothing else the runner holds.
/// </summary>
public sealed class ShellEnvironmentTests
{
    private static StubEnvironment OperatorSession()
    {
        var environment = new StubEnvironment();
        environment.Variables["PATH"] = "/usr/bin:/bin";
        environment.Variables["HOME"] = "/home/operator";
        environment.Variables["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/run/user/1000/bus";
        environment.Variables["XDG_RUNTIME_DIR"] = "/run/user/1000";
        environment.Variables["SSH_AUTH_SOCK"] = "/run/user/1000/ssh-agent.socket";
        environment.Variables["OPENAI_API_KEY"] = "sk-secret";
        environment.Variables["ORKEON_Llm__ApiKey"] = "secret";
        environment.Variables["GH_TOKEN"] = "ghp_secret";
        return environment;
    }

    [Fact]
    public void ShouldPropagateTheSessionSockets_WhenTheParentHasThem()
    {
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, includeSessionSockets: true);

        Assert.Equal("unix:path=/run/user/1000/bus", variables["DBUS_SESSION_BUS_ADDRESS"]);
        Assert.Equal("/run/user/1000", variables["XDG_RUNTIME_DIR"]);
        Assert.Equal("/run/user/1000/ssh-agent.socket", variables["SSH_AUTH_SOCK"]);
        Assert.Equal("/usr/bin:/bin", variables["PATH"]);
    }

    [Fact]
    public void ShouldStillDropTheSecrets_WhenTheSessionSocketsArePropagated()
    {
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, includeSessionSockets: true);

        Assert.DoesNotContain("OPENAI_API_KEY", variables.Keys);
        Assert.DoesNotContain("ORKEON_Llm__ApiKey", variables.Keys);
        Assert.DoesNotContain("GH_TOKEN", variables.Keys);
        Assert.DoesNotContain("sk-secret", variables.Values);
    }

    [Fact]
    public void ShouldLeaveTheSessionSocketsOut_WhenAsked()
    {
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, includeSessionSockets: false);

        Assert.DoesNotContain("DBUS_SESSION_BUS_ADDRESS", variables.Keys);
        Assert.DoesNotContain("XDG_RUNTIME_DIR", variables.Keys);
        Assert.DoesNotContain("SSH_AUTH_SOCK", variables.Keys);
        Assert.Equal("/home/operator", variables["HOME"]);
    }

    [Fact]
    public void ShouldSetNoSessionSocket_WhenTheParentHasNone()
    {
        var environment = new StubEnvironment();
        environment.Variables["PATH"] = "/usr/bin";

        var variables = ShellEnvironment.RestrictedVariables(environment.Read, includeSessionSockets: true);

        Assert.DoesNotContain("DBUS_SESSION_BUS_ADDRESS", variables.Keys);
        Assert.DoesNotContain("SSH_AUTH_SOCK", variables.Keys);
    }

    [Fact]
    public void ShouldReplaceTheInheritedEnvironment_WhenConfiguringAStartInfo()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("/bin/sh");
        startInfo.Environment["OPENAI_API_KEY"] = "sk-inherited";

        ShellEnvironment.ConfigureRestricted(startInfo, OperatorSession().Read, includeSessionSockets: true);

        Assert.DoesNotContain("OPENAI_API_KEY", startInfo.Environment.Keys);
        Assert.Equal("/run/user/1000/ssh-agent.socket", startInfo.Environment["SSH_AUTH_SOCK"]);
    }

    [Theory]
    [InlineData("unix:path=/run/user/1000/bus", "/run/user/1000/bus")]
    [InlineData("unix:path=/run/user/1000/bus,guid=0123abcd", "/run/user/1000/bus")]
    [InlineData("unix:guid=0123abcd,path=/run/user/1000/bus", "/run/user/1000/bus")]
    [InlineData("unix:path=/run/user/1000/my%20bus", "/run/user/1000/my bus")]
    [InlineData("tcp:host=localhost,port=1234;unix:path=/run/dbus/bus", "/run/dbus/bus")]
    public void ShouldFindTheBusSocketPath_WhenTheAddressNamesAUnixPath(string address, string expected)
    {
        var environment = new StubEnvironment();
        environment.Variables["DBUS_SESSION_BUS_ADDRESS"] = address;

        Assert.Equal([expected], ShellEnvironment.SessionSocketPaths(environment.Read));
    }

    [Theory]
    [InlineData("unix:abstract=/tmp/dbus-abc,guid=0123")]
    [InlineData("tcp:host=localhost,port=1234")]
    [InlineData("unix:path=relative/bus")]
    [InlineData("unix:path=/run/user/1000/../../etc")]
    [InlineData("unix:path=/")]
    [InlineData("garbage")]
    public void ShouldFindNoBusSocketPath_WhenTheAddressNamesNoAbsoluteUnixPath(string address)
    {
        var environment = new StubEnvironment();
        environment.Variables["DBUS_SESSION_BUS_ADDRESS"] = address;

        Assert.Empty(ShellEnvironment.SessionSocketPaths(environment.Read));
    }

    [Fact]
    public void ShouldFindTheAgentSocketPath_WhenSshAuthSockIsAbsolute()
    {
        var environment = new StubEnvironment();
        environment.Variables["SSH_AUTH_SOCK"] = "/tmp/ssh-XXXX/agent.42";

        Assert.Equal(["/tmp/ssh-XXXX/agent.42"], ShellEnvironment.SessionSocketPaths(environment.Read));
    }

    [Theory]
    [InlineData("agent.42")]
    [InlineData("/home/operator/../../etc/agent")]
    [InlineData("/")]
    public void ShouldFindNoAgentSocketPath_WhenSshAuthSockIsNotANormalizedAbsolutePath(string value)
    {
        var environment = new StubEnvironment();
        environment.Variables["SSH_AUTH_SOCK"] = value;

        Assert.Empty(ShellEnvironment.SessionSocketPaths(environment.Read));
    }

    [Fact]
    public void ShouldNeverNameTheRuntimeDirectoryAsASocketPath()
    {
        var paths = ShellEnvironment.SessionSocketPaths(OperatorSession().Read);

        Assert.Equal(["/run/user/1000/ssh-agent.socket", "/run/user/1000/bus"], paths);
        Assert.DoesNotContain("/run/user/1000", paths);
    }
}
