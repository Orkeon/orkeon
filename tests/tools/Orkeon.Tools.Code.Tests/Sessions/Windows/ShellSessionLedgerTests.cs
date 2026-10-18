using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions.Windows;

/// <summary>
/// <see cref="ShellSessionLedger"/> and <see cref="ShellSessionLedgerStore"/>: the JSON round trip, the
/// refusal of a file that is not a ledger of ours, the owner's liveness, and the folder of ledgers.
/// </summary>
public sealed class ShellSessionLedgerTests : IDisposable
{
    private static readonly ShellSessionLedger s_ledger = new()
    {
        ProfileName = "Orkeon.Shell.0123456789abcdef0123456789abcdef",
        ContainerSid = "S-1-15-2-1-2-3-4-5-6-7",
        HostPid = 4242,
        HostStartTicks = 638_000_000_000_000_000,
        GrantedPaths = [@"C:\work\repo", @"C:\work\repo\secrets"],
    };

    private readonly string _directory = Directory.CreateTempSubdirectory("orkeon-ledgers-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void ShouldReadBackWhatItWrote()
    {
        var read = ShellSessionLedger.Deserialize(s_ledger.Serialize());

        Assert.NotNull(read);
        Assert.Equal(s_ledger.ProfileName, read.ProfileName);
        Assert.Equal(s_ledger.ContainerSid, read.ContainerSid);
        Assert.Equal(s_ledger.HostPid, read.HostPid);
        Assert.Equal(s_ledger.HostStartTicks, read.HostStartTicks);
        Assert.Equal(s_ledger.GrantedPaths, read.GrantedPaths);
    }

    [Fact]
    public void ShouldWriteSnakeCaseNames()
    {
        var json = s_ledger.Serialize();

        Assert.Contains("\"profile_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"container_sid\"", json, StringComparison.Ordinal);
        Assert.Contains("\"host_pid\"", json, StringComparison.Ordinal);
        Assert.Contains("\"host_start_ticks\"", json, StringComparison.Ordinal);
        Assert.Contains("\"granted_paths\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"profile_name\":\"Other.App\",\"container_sid\":\"S-1-15-2-1\",\"host_pid\":1,\"host_start_ticks\":1}")]
    [InlineData("{\"profile_name\":\"Orkeon.Shell.x\",\"container_sid\":\"S-1-5-18\",\"host_pid\":1,\"host_start_ticks\":1}")]
    [InlineData("{\"profile_name\":\"Orkeon.Shell.x\",\"host_pid\":1,\"host_start_ticks\":1}")]
    public void ShouldRefuseWhatIsNotALedgerOfOurs(string json)
        => Assert.Null(ShellSessionLedger.Deserialize(json));

    [Fact]
    public void ShouldBeStale_WhenNoProcessHasThePid()
        => Assert.True(s_ledger.IsStale(_ => null));

    [Fact]
    public void ShouldBeStale_WhenThePidWasReusedByAnotherProcess()
        => Assert.True(s_ledger.IsStale(_ => s_ledger.HostStartTicks + 1));

    [Fact]
    public void ShouldNotBeStale_WhileItsHostRuns()
        => Assert.False(s_ledger.IsStale(pid => pid == s_ledger.HostPid ? s_ledger.HostStartTicks : null));

    [Fact]
    public void ShouldKeepOneFilePerSession_AndDeleteIt()
    {
        var store = new ShellSessionLedgerStore(Path.Combine(_directory, "shell-sessions"));
        Assert.Empty(store.ReadAll());

        store.Write(s_ledger);
        store.Write(s_ledger with { HostPid = 7 });

        var (name, ledger) = Assert.Single(store.ReadAll());
        Assert.Equal(s_ledger.ProfileName, name);
        Assert.Equal(7, ledger!.HostPid);

        store.Delete(s_ledger.ProfileName);
        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public void ShouldListAnUnreadableLedgerWithoutContent_AndIgnoreForeignFiles()
    {
        var store = new ShellSessionLedgerStore(_directory);
        File.WriteAllText(Path.Combine(_directory, "Orkeon.Shell.broken.json"), "{ truncated");
        File.WriteAllText(Path.Combine(_directory, "notes.json"), "{}");

        var (name, ledger) = Assert.Single(store.ReadAll());

        Assert.Equal("Orkeon.Shell.broken", name);
        Assert.Null(ledger);
    }
}
