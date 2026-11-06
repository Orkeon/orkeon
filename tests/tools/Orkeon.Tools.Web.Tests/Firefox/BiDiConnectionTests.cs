using System.Text.Json.Nodes;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Web.Firefox.BiDi;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class BiDiConnectionTests
{
    private static readonly TimeSpan NeverReached = Polling.DefaultTimeout;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(string Method, JsonObject Params)> NextCommandAsync(FakeBiDiTransport transport, int count)
    {
        await Polling.WaitUntilAsync(() => transport.Commands.Count >= count);
        return transport.Commands[count - 1];
    }

    [Fact]
    public async Task Should_SendTheMethodAndItsParameters_AndReturnTheResult()
    {
        await using var transport = new FakeBiDiTransport();
        transport.Handlers["browsingContext.navigate"] = p => new JsonObject { ["url"] = p["url"]!.GetValue<string>() };
        await using var connection = new BiDiConnection(transport, NeverReached);

        var result = await connection.SendAsync(
            "browsingContext.navigate", new JsonObject { ["url"] = "https://example.test/" }, Ct);

        Assert.Equal("https://example.test/", result["url"]!.GetValue<string>());
        var (method, parameters) = Assert.Single(transport.Commands);
        Assert.Equal("browsingContext.navigate", method);
        Assert.Equal("https://example.test/", parameters["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_SendAnEmptyParameterObject_When_NoneIsGiven()
    {
        await using var transport = new FakeBiDiTransport();
        await using var connection = new BiDiConnection(transport, NeverReached);

        var result = await connection.SendAsync("browser.close", null, Ct);

        Assert.Empty(result);
        Assert.Empty(Assert.Single(transport.Commands).Params);
    }

    [Fact]
    public async Task Should_GiveEachCallerItsOwnAnswer_When_AnswersComeOutOfOrder()
    {
        await using var transport = new FakeBiDiTransport { AutoAnswer = false };
        await using var connection = new BiDiConnection(transport, NeverReached);

        var first = connection.SendAsync("a", null, Ct);
        await NextCommandAsync(transport, 1);
        var second = connection.SendAsync("b", null, Ct);
        await NextCommandAsync(transport, 2);
        transport.Push("""{"type":"success","id":2,"result":{"who":"second"}}""");
        transport.Push("""{"type":"success","id":1,"result":{"who":"first"}}""");

        Assert.Equal("second", (await second)["who"]!.GetValue<string>());
        Assert.Equal("first", (await first)["who"]!.GetValue<string>());
    }

    [Fact]
    public async Task Should_ThrowTheBrowserError_When_ACommandIsRefused()
    {
        await using var transport = new FakeBiDiTransport();
        transport.Handlers["input.performActions"] = _ => throw new FakeBiDiError("no such node", "The node is not known");
        await using var connection = new BiDiConnection(transport, NeverReached);

        var error = await Assert.ThrowsAsync<BiDiException>(() => connection.SendAsync("input.performActions", null, Ct));

        Assert.Equal(BiDiException.NoSuchNode, error.Code);
        Assert.Equal("no such node: The node is not known", error.Message);
        Assert.IsType<IExpectedToolError>(error, exactMatch: false);
    }

    [Fact]
    public async Task Should_DropEventsUnknownIdsAndUnreadableMessages()
    {
        await using var transport = new FakeBiDiTransport { AutoAnswer = false };
        await using var connection = new BiDiConnection(transport, NeverReached);

        var pending = connection.SendAsync("a", null, Ct);
        await NextCommandAsync(transport, 1);
        transport.Push("""{"type":"event","method":"log.entryAdded","params":{}}""");
        transport.Push("""{"type":"success","id":99,"result":{}}""");
        transport.Push("""{"type":"error","id":null,"error":"unknown error","message":"x"}""");
        transport.Push("not json");
        transport.Push("""{"type":"success","id":1,"result":{"ok":true}}""");

        Assert.True((await pending)["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Should_FailThePendingCommands_AndRefuseNewOnes_When_TheBrowserClosesTheChannel()
    {
        await using var transport = new FakeBiDiTransport { AutoAnswer = false };
        await using var connection = new BiDiConnection(transport, NeverReached);

        var pending = connection.SendAsync("a", null, Ct);
        await NextCommandAsync(transport, 1);
        transport.Close();

        var lost = await Assert.ThrowsAsync<BiDiException>(() => pending);
        Assert.Equal(BiDiException.ConnectionClosed, lost.Code);
        await Polling.WaitUntilAsync(() => connection.IsClosed);
        var refused = await Assert.ThrowsAsync<BiDiException>(() => connection.SendAsync("b", null, Ct));
        Assert.Equal(BiDiException.ConnectionClosed, refused.Code);
        Assert.Single(transport.Commands);
    }

    [Fact]
    public async Task Should_ReportAClosedConnection_When_TheMessageCannotBeSent()
    {
        await using var transport = new FakeBiDiTransport { SendFailure = new InvalidOperationException("socket gone") };
        await using var connection = new BiDiConnection(transport, NeverReached);

        var error = await Assert.ThrowsAsync<BiDiException>(() => connection.SendAsync("a", null, Ct));

        Assert.Equal(BiDiException.ConnectionClosed, error.Code);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Fact]
    public async Task Should_Cancel_When_TheCallerCancels()
    {
        await using var transport = new FakeBiDiTransport { AutoAnswer = false };
        await using var connection = new BiDiConnection(transport, NeverReached);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var pending = connection.SendAsync("a", null, caller.Token);
        await NextCommandAsync(transport, 1);
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Should_ThrowATimeout_When_TheBrowserDoesNotAnswer()
    {
        await using var transport = new FakeBiDiTransport { AutoAnswer = false };
        await using var connection = new BiDiConnection(transport, TimeSpan.FromMilliseconds(20));

        var error = await Assert.ThrowsAsync<BiDiException>(() => connection.SendAsync("script.evaluate", null, Ct));

        Assert.Equal(BiDiException.Timeout, error.Code);
        Assert.Contains("script.evaluate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_DisposeTheTransport_AndEndThePendingCommands_When_Disposed()
    {
        await using var transport = new FakeBiDiTransport { AutoAnswer = false };
        var connection = new BiDiConnection(transport, NeverReached);
        var pending = connection.SendAsync("a", null, Ct);
        await NextCommandAsync(transport, 1);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        Assert.True(transport.Disposed);
        Assert.True(connection.IsClosed);
        await Assert.ThrowsAsync<BiDiException>(() => pending);
    }
}
