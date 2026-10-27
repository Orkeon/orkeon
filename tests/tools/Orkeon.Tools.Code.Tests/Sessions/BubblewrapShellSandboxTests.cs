using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Sessions;
using Orkeon.Tools.Code.Tests.Doubles;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// The <c>bwrap</c> start of a confined session and the sockets of the operator's session (#216):
/// with the network, the socket paths alone are bound read-write and the variables follow; without
/// it, neither. Built without starting bwrap, so no host bubblewrap is needed.
/// </summary>
public sealed class BubblewrapShellSandboxTests
{
    private const string Bwrap = "/usr/bin/bwrap";

    private static StubEnvironment OperatorSession(string busAddress = "unix:path=/run/user/1000/bus,guid=0123")
    {
        var environment = new StubEnvironment();
        environment.Variables["PATH"] = "/usr/bin:/bin";
        environment.Variables["DBUS_SESSION_BUS_ADDRESS"] = busAddress;
        environment.Variables["XDG_RUNTIME_DIR"] = "/run/user/1000";
        environment.Variables["SSH_AUTH_SOCK"] = "/run/user/1000/ssh-agent.socket";
        environment.Variables["OPENAI_API_KEY"] = "sk-secret";
        return environment;
    }

    private static BubblewrapShellSandbox Sandbox(StubEnvironment environment, Func<string, ConfinedAccess?>? kindOf = null)
        => new(() => BubblewrapAvailability.Available(Bwrap), environment.Read, kindOf ?? (_ => ConfinedAccess.HiddenFile));

    private static ShellSessionView View(bool allowNetwork) => new([], [], allowNetwork);

    private static int IndexOf(IList<string> arguments, params string[] sequence)
    {
        for (var i = 0; i + sequence.Length <= arguments.Count; i++)
        {
            if (sequence.Select((value, offset) => arguments[i + offset] == value).All(match => match))
                return i;
        }

        return -1;
    }

    [Fact]
    public void ShouldBindTheSocketPathsReadWriteAndPropagateTheVariables_WhenTheNetworkIsAllowed()
    {
        var info = Sandbox(OperatorSession()).PrepareStartInfo(View(allowNetwork: true), Bwrap);

        Assert.True(IndexOf(info.ArgumentList, "--bind-try", "/run/user/1000/ssh-agent.socket", "/run/user/1000/ssh-agent.socket") >= 0);
        Assert.True(IndexOf(info.ArgumentList, "--bind-try", "/run/user/1000/bus", "/run/user/1000/bus") >= 0);
        Assert.Equal("/run/user/1000/ssh-agent.socket", info.Environment["SSH_AUTH_SOCK"]);
        Assert.Equal("unix:path=/run/user/1000/bus,guid=0123", info.Environment["DBUS_SESSION_BUS_ADDRESS"]);
        Assert.Equal("/run/user/1000", info.Environment["XDG_RUNTIME_DIR"]);
    }

    [Fact]
    public void ShouldNeverBindTheRuntimeDirectory()
    {
        var info = Sandbox(OperatorSession()).PrepareStartInfo(View(allowNetwork: true), Bwrap);

        Assert.DoesNotContain("/run/user/1000", info.ArgumentList);
    }

    [Fact]
    public void ShouldNeitherBindNorPropagateTheSockets_WhenTheNetworkIsNotAllowed()
    {
        var info = Sandbox(OperatorSession()).PrepareStartInfo(View(allowNetwork: false), Bwrap);

        Assert.DoesNotContain("/run/user/1000/ssh-agent.socket", info.ArgumentList);
        Assert.DoesNotContain("/run/user/1000/bus", info.ArgumentList);
        Assert.DoesNotContain("SSH_AUTH_SOCK", info.Environment.Keys);
        Assert.DoesNotContain("DBUS_SESSION_BUS_ADDRESS", info.Environment.Keys);
        Assert.DoesNotContain("XDG_RUNTIME_DIR", info.Environment.Keys);
    }

    [Fact]
    public void ShouldBindNothingForAnAbstractBus_AndStillPropagateItsAddress()
    {
        var info = Sandbox(OperatorSession("unix:abstract=/tmp/dbus-abc,guid=0123")).PrepareStartInfo(View(allowNetwork: true), Bwrap);

        Assert.DoesNotContain("/tmp/dbus-abc", info.ArgumentList);
        Assert.Equal("unix:abstract=/tmp/dbus-abc,guid=0123", info.Environment["DBUS_SESSION_BUS_ADDRESS"]);
    }

    [Fact]
    public void ShouldNotBindASocketPath_WhenItIsADirectoryOrAbsent()
    {
        var sandbox = Sandbox(OperatorSession(), path => path == "/run/user/1000/bus" ? ConfinedAccess.HiddenDirectory : null);

        var info = sandbox.PrepareStartInfo(View(allowNetwork: true), Bwrap);

        Assert.DoesNotContain("/run/user/1000/bus", info.ArgumentList);
        Assert.DoesNotContain("/run/user/1000/ssh-agent.socket", info.ArgumentList);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldNeverHandTheShellASecret(bool allowNetwork)
    {
        var info = Sandbox(OperatorSession()).PrepareStartInfo(View(allowNetwork), Bwrap);

        Assert.DoesNotContain("OPENAI_API_KEY", info.Environment.Keys);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldPassAnAddedVariableWithoutBindingThePathItNames_WhateverTheNetwork(bool allowNetwork)
    {
        var environment = OperatorSession();
        environment.Variables["SSL_CERT_DIR"] = "/opt/certs/bundle";
        var view = View(allowNetwork) with { PropagatedVariables = ["SSL_CERT_DIR"] };

        var info = Sandbox(environment).PrepareStartInfo(view, Bwrap);

        Assert.Equal("/opt/certs/bundle", info.Environment["SSL_CERT_DIR"]);
        Assert.DoesNotContain("/opt/certs/bundle", info.ArgumentList);
    }
}
