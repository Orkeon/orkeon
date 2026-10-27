using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
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
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, ShellSessionSockets.All);

        Assert.Equal("unix:path=/run/user/1000/bus", variables["DBUS_SESSION_BUS_ADDRESS"]);
        Assert.Equal("/run/user/1000", variables["XDG_RUNTIME_DIR"]);
        Assert.Equal("/run/user/1000/ssh-agent.socket", variables["SSH_AUTH_SOCK"]);
        Assert.Equal("/usr/bin:/bin", variables["PATH"]);
    }

    [Fact]
    public void ShouldStillDropTheSecrets_WhenTheSessionSocketsArePropagated()
    {
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, ShellSessionSockets.All);

        Assert.DoesNotContain("OPENAI_API_KEY", variables.Keys);
        Assert.DoesNotContain("ORKEON_Llm__ApiKey", variables.Keys);
        Assert.DoesNotContain("GH_TOKEN", variables.Keys);
        Assert.DoesNotContain("sk-secret", variables.Values);
    }

    [Fact]
    public void ShouldLeaveTheSessionSocketsOut_WhenAsked()
    {
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, ShellSessionSockets.None);

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

        var variables = ShellEnvironment.RestrictedVariables(environment.Read, ShellSessionSockets.All);

        Assert.DoesNotContain("DBUS_SESSION_BUS_ADDRESS", variables.Keys);
        Assert.DoesNotContain("SSH_AUTH_SOCK", variables.Keys);
    }

    [Fact]
    public void ShouldReplaceTheInheritedEnvironment_WhenConfiguringAStartInfo()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("/bin/sh");
        startInfo.Environment["OPENAI_API_KEY"] = "sk-inherited";

        ShellEnvironment.ConfigureRestricted(startInfo, OperatorSession().Read, ShellSessionSockets.All);

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

        Assert.Equal([expected], ShellEnvironment.SessionSocketPaths(environment.Read, includeBus: true));
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

        Assert.Empty(ShellEnvironment.SessionSocketPaths(environment.Read, includeBus: true));
    }

    [Fact]
    public void ShouldFindTheAgentSocketPath_WhenSshAuthSockIsAbsolute()
    {
        var environment = new StubEnvironment();
        environment.Variables["SSH_AUTH_SOCK"] = "/tmp/ssh-XXXX/agent.42";

        Assert.Equal(["/tmp/ssh-XXXX/agent.42"], ShellEnvironment.SessionSocketPaths(environment.Read, includeBus: true));
    }

    [Theory]
    [InlineData("agent.42")]
    [InlineData("/home/operator/../../etc/agent")]
    [InlineData("/")]
    public void ShouldFindNoAgentSocketPath_WhenSshAuthSockIsNotANormalizedAbsolutePath(string value)
    {
        var environment = new StubEnvironment();
        environment.Variables["SSH_AUTH_SOCK"] = value;

        Assert.Empty(ShellEnvironment.SessionSocketPaths(environment.Read, includeBus: true));
    }

    [Fact]
    public void ShouldNeverNameTheRuntimeDirectoryAsASocketPath()
    {
        var paths = ShellEnvironment.SessionSocketPaths(OperatorSession().Read, includeBus: true);

        Assert.Equal(["/run/user/1000/ssh-agent.socket", "/run/user/1000/bus"], paths);
        Assert.DoesNotContain("/run/user/1000", paths);
    }
    [Fact]
    public void ShouldPropagateAnAddedName_WhenTheOperatorConfiguresIt()
    {
        var environment = OperatorSession();
        environment.Variables["HTTPS_PROXY"] = "http://proxy.local:3128";
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated(["HTTPS_PROXY"], logger);
        var variables = ShellEnvironment.RestrictedVariables(environment.Read, ShellSessionSockets.All, propagated);

        Assert.Equal(["HTTPS_PROXY"], propagated);
        Assert.Equal("http://proxy.local:3128", variables["HTTPS_PROXY"]);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData("ORKEON_Llm__Model")]
    [InlineData("orkeon_anything")]
    public void ShouldRefuseAnOrkeonName_WithAWarning(string name)
    {
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated([name], logger);

        Assert.Empty(propagated);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GH_TOKEN")]
    [InlineData("OPENAI_API_KEY")]
    [InlineData("my_secret_dir")]
    [InlineData("DB_PASSWORD")]
    [InlineData("SMB_PASSWD")]
    [InlineData("AWS_CREDENTIALS_FILE")]
    [InlineData("PROXY_AUTH")]
    [InlineData("gh_token")]
    [InlineData("Proxy_Password")]
    public void ShouldRefuseASecretWordName_WithAWarning(string name)
    {
        var environment = OperatorSession();
        environment.Variables[name] = "secret";
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated([name], logger);
        var variables = ShellEnvironment.RestrictedVariables(environment.Read, ShellSessionSockets.All, [name]);

        Assert.Empty(propagated);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(name, StringComparison.Ordinal));
        Assert.DoesNotContain(name, variables.Keys);
    }

    [Theory]
    [InlineData("SSH_AUTH_SOCK")]
    [InlineData("PATH")]
    [InlineData("DBUS_SESSION_BUS_ADDRESS")]
    [InlineData("DOTNET_NOLOGO")]
    public void ShouldIgnoreADuplicateOfABuiltInName_WithoutAWarning(string name)
    {
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated([name], logger);

        Assert.Empty(propagated);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A=B")]
    [InlineData("A<NUL>B")]
    public void ShouldRefuseAnInvalidName_WithAWarning(string name)
    {
        // The NUL is spelled out in the data, so the theory serializes plainly.
        name = name.Replace("<NUL>", "\0", StringComparison.Ordinal);
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated([name], logger);

        Assert.Empty(propagated);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void ShouldKeepEachAddedNameOnce_WhenTheOperatorRepeatsIt()
    {
        var propagated = ShellEnvironment.SelectPropagated(["HTTPS_PROXY", "HTTPS_PROXY", "NO_PROXY"], new RecordingLogger());

        Assert.Equal(["HTTPS_PROXY", "NO_PROXY"], propagated);
    }

    [Fact]
    public void ShouldLeaveTheBusOut_ButKeepTheAgent_WhenTheSocketsComeWithoutTheBus()
    {
        var variables = ShellEnvironment.RestrictedVariables(OperatorSession().Read, ShellSessionSockets.WithoutBus);

        Assert.DoesNotContain("DBUS_SESSION_BUS_ADDRESS", variables.Keys);
        Assert.Equal("/run/user/1000/ssh-agent.socket", variables["SSH_AUTH_SOCK"]);
        Assert.Equal("/run/user/1000", variables["XDG_RUNTIME_DIR"]);
    }

    [Fact]
    public void ShouldNameOnlyTheAgentSocketPath_WhenTheBusIsLeftOut()
    {
        Assert.Equal(["/run/user/1000/ssh-agent.socket"], ShellEnvironment.SessionSocketPaths(OperatorSession().Read, includeBus: false));
    }

    [Fact]
    public void ShouldKeepTwoNamesDifferingByCase_ExceptOnWindows()
    {
        var environment = OperatorSession();
        environment.Variables["http_proxy"] = "http://lower.local:3128";
        environment.Variables["HTTP_PROXY"] = "http://upper.local:3128";

        var propagated = ShellEnvironment.SelectPropagated(["http_proxy", "HTTP_PROXY"], environment.Read, new RecordingLogger());
        var variables = ShellEnvironment.RestrictedVariables(environment.Read, ShellSessionSockets.All, propagated);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(["http_proxy"], propagated);
        }
        else
        {
            Assert.Equal(["http_proxy", "HTTP_PROXY"], propagated);
            Assert.Equal("http://lower.local:3128", variables["http_proxy"]);
            Assert.Equal("http://upper.local:3128", variables["HTTP_PROXY"]);
        }
    }

    [Fact]
    public void ShouldTakeALowerCaseBuiltInNameForAnotherVariable_ExceptOnWindows()
    {
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated(["path"], logger);

        Assert.Equal(OperatingSystem.IsWindows() ? [] : ["path"], propagated);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("http://user:s3cr3t@proxy.local:3128")]
    [InlineData("https://s3cr3t@github.com/org/repo")]
    [InlineData("socks5://u:s3cr3t@proxy.local:1080")]
    [InlineData("http://proxy.local:3128,http://u:s3cr3t@other.local")]
    public void ShouldDropAnAddedVariable_WithAWarningThatHidesTheValue_WhenItsValueCarriesUrlCredentials(string value)
    {
        var environment = OperatorSession();
        environment.Variables["HTTPS_PROXY"] = value;
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated(["HTTPS_PROXY"], environment.Read, logger);
        var variables = ShellEnvironment.RestrictedVariables(environment.Read, ShellSessionSockets.All, ["HTTPS_PROXY"]);

        Assert.Empty(propagated);
        Assert.DoesNotContain("HTTPS_PROXY", variables.Keys);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("HTTPS_PROXY", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://proxy.local:3128")]
    [InlineData("/opt/certs")]
    [InlineData("operator@example.com")]
    public void ShouldKeepAnAddedVariable_WhenItsValueCarriesNoUrlCredentials(string value)
    {
        var environment = OperatorSession();
        environment.Variables["HTTPS_PROXY"] = value;
        var logger = new RecordingLogger();

        var propagated = ShellEnvironment.SelectPropagated(["HTTPS_PROXY"], environment.Read, logger);

        Assert.Equal(["HTTPS_PROXY"], propagated);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ShouldWarnThatTheBusLeavesTheSandbox_WhenTheSessionBusIsEnabled()
    {
        var options = new ShellEnvironmentOptions { SessionBus = true };
        var logger = new RecordingLogger();

        var selected = ShellPropagatedVariables.From(options, new StubEnvironment().Read, logger);

        Assert.True(selected.SessionBus);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("SessionBus", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLeaveTheSessionBusOffWithoutAWarning_ByDefault()
    {
        var logger = new RecordingLogger();

        var selected = ShellPropagatedVariables.From(new ShellEnvironmentOptions(), new StubEnvironment().Read, logger);

        Assert.False(selected.SessionBus);
        Assert.Empty(logger.Entries);
    }
}
