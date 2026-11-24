using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Orkeon.Tools.Web.Chromium;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Chromium;

public sealed class ChromiumBiDiTransportTests : IAsyncDisposable
{
    private const string Session = "S1";

    private readonly FakeBiDiTransport _devTools = new();

    public ChromiumBiDiTransportTests()
    {
        _devTools.Handlers["Target.getTargets"] = _ => Targets("page");
        _devTools.Handlers["Target.createTarget"] = _ => new JsonObject { ["targetId"] = "T1" };
        _devTools.Handlers["Target.attachToTarget"] = _ => new JsonObject { ["sessionId"] = Session };
    }

    public ValueTask DisposeAsync() => _devTools.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Targets(params string[] types) =>
        new() { ["targetInfos"] = new JsonArray([.. types.Select(type => new JsonObject { ["type"] = type })]) };

    private Task<ChromiumBiDiTransport> StartAsync() => ChromiumBiDiTransport.StartAsync(_devTools, "the script", Ct);

    private static string Binding(string session, string payload) => new JsonObject
    {
        ["method"] = "Runtime.bindingCalled",
        ["sessionId"] = session,
        ["params"] = new JsonObject { ["name"] = "sendBidiResponse", ["payload"] = payload },
    }.ToJsonString();

    [Fact]
    public void Should_EmbedTheMapper_AsPublished()
    {
        using var stream = typeof(ChromiumMapper).Assembly.GetManifestResourceStream("Orkeon.Tools.Web.Chromium.mapperTab.js");

        Assert.NotNull(stream);
        Assert.Equal(ChromiumMapper.Sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
        Assert.Contains("runMapperInstance", ChromiumMapper.Script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_RunTheMapperInAHiddenTab_When_Started()
    {
        await using var transport = await StartAsync();

        Assert.Equal(
            ["Target.getTargets", "Target.createTarget", "Target.attachToTarget", "Runtime.enable", "Target.exposeDevToolsProtocol", "Runtime.addBinding", "Runtime.evaluate", "Runtime.evaluate"],
            _devTools.Methods);
        Assert.True(Assert.Single(_devTools.ParamsOf("Target.createTarget"))["hidden"]!.GetValue<bool>());
        var evaluated = _devTools.ParamsOf("Runtime.evaluate").Select(parameters => parameters["expression"]!.GetValue<string>()).ToList();
        Assert.Equal("the script", evaluated[0]);
        Assert.Contains("runMapperInstance(\"T1\")", evaluated[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_WaitForTheFirstTab_BeforeTheMapperStarts()
    {
        var asked = 0;
        _devTools.Handlers["Target.getTargets"] = _ => ++asked < 3 ? Targets("service_worker") : Targets("service_worker", "page");

        await using var transport = await StartAsync();

        Assert.Equal(3, asked);
    }

    [Fact]
    public async Task Should_FailTheStart_AndCloseTheChannel_When_TheMapperThrows()
    {
        _devTools.Handlers["Runtime.evaluate"] = _ => new JsonObject
        {
            ["exceptionDetails"] = new JsonObject { ["exception"] = new JsonObject { ["description"] = "Error: no cdp" } },
        };

        var error = await Assert.ThrowsAsync<FirefoxException>(StartAsync);

        Assert.Contains("Error: no cdp", error.Message, StringComparison.Ordinal);
        Assert.True(_devTools.Disposed);
    }

    [Fact]
    public async Task Should_FailTheStart_When_TheBrowserRefusesAStep()
    {
        _devTools.AutoAnswer = false;
        _devTools.Push("""{"id":-1,"error":{"code":-32601,"message":"not found"}}""");

        var error = await Assert.ThrowsAsync<FirefoxException>(StartAsync);

        Assert.Contains("'Target.getTargets': not found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_FailTheStart_When_TheBrowserGoesAway()
    {
        _devTools.AutoAnswer = false;
        _devTools.Close();

        await Assert.ThrowsAsync<FirefoxException>(StartAsync);
    }

    [Fact]
    public async Task Should_HandAMessageToTheMapper_AsAStringOfTheScript()
    {
        await using var transport = await StartAsync();
        const string Message = """{"id":4,"method":"script.evaluate","params":{"expression":"'\"); evil(\"'"}}""";

        await transport.SendAsync(Message, Ct);

        var expression = _devTools.ParamsOf("Runtime.evaluate")[^1]["expression"]!.GetValue<string>();
        Assert.StartsWith("onBidiMessage(\"", expression, StringComparison.Ordinal);
        Assert.Equal(Message, JsonNode.Parse(expression["onBidiMessage(".Length..^1])!.GetValue<string>());
    }

    [Fact]
    public async Task Should_GiveBackWhatTheMapperSays_AndNothingElse()
    {
        await using var transport = await StartAsync();
        _devTools.Push("""{"method":"Runtime.consoleAPICalled","sessionId":"S1","params":{}}""");
        _devTools.Push(Binding("another", "not for us"));
        _devTools.Push(Binding(Session, "an event"));

        Assert.Equal("an event", await transport.ReceiveAsync(Ct));
    }

    [Theory]
    [InlineData("""{"id":7,"error":{"message":"no session"}}""", "no session")]
    [InlineData("""{"id":7,"result":{"exceptionDetails":{"text":"Uncaught"}}}""", "Uncaught")]
    public async Task Should_AnswerInTheMappersName_When_ItCouldNotBeHandedACommand(string answer, string reason)
    {
        await using var transport = await StartAsync();
        _devTools.Push("""{"id":6,"result":{"result":{"type":"undefined"}}}""");
        _devTools.Push(answer);

        var refusal = JsonNode.Parse((await transport.ReceiveAsync(Ct))!)!;

        Assert.Equal("error", refusal["type"]!.GetValue<string>());
        Assert.Equal(7, refusal["id"]!.GetValue<long>());
        Assert.Equal(reason, refusal["message"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"method":"Target.detachedFromTarget","params":{"sessionId":"S1"}}""")]
    [InlineData("""{"method":"Inspector.targetCrashed","sessionId":"S1","params":{}}""")]
    public async Task Should_EndTheChannel_When_TheTabOfTheMapperIsGone(string message)
    {
        await using var transport = await StartAsync();
        _devTools.Push("""{"method":"Target.detachedFromTarget","params":{"sessionId":"another"}}""");
        _devTools.Push(message);

        Assert.Null(await transport.ReceiveAsync(Ct));
    }
}
