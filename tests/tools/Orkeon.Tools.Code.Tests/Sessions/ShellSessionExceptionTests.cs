using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary><see cref="ShellSessionException"/>: the three standard constructors.</summary>
public sealed class ShellSessionExceptionTests
{
    [Fact]
    public void ShouldCarryAMessageAndACause_WhenASessionFails()
    {
        var cause = new IOException("pipe closed");

        Assert.NotNull(new ShellSessionException().Message);
        Assert.Equal("closed", new ShellSessionException("closed").Message);
        Assert.Same(cause, new ShellSessionException("died", cause).InnerException);
    }
}
