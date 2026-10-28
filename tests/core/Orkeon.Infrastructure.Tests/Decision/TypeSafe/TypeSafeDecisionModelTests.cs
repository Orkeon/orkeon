using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Decision.TypeSafe;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Resilience;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;

namespace Orkeon.Infrastructure.Tests.Decision.TypeSafe;

public class TypeSafeDecisionModelTests
{
    private const string Key = "ts-test-key";

    private const string ChoiceBody = """
        {"model":"jev-1.14.0","answers":{"tone":{"type":"choice","choice":"calm","confidence":0.91,
        "probabilities":{"calm":0.91,"angry":0.09}}},"usage":{"input_tokens":42,"output_tokens":0}}
        """;

    private static readonly LlmKeyEnvironment NoKeys = new(_ => null, _ => null);

    private static DecisionRequest ChoiceRequest(bool images = false, bool longContext = false) => new()
    {
        State = JsonValue.Create("I am fine, thanks."),
        Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add("tone", new ChoiceQuestion
        {
            Criteria = ImmutableDictionary<string, JsonNode?>.Empty
                .Add("calm", JsonValue.Create("The text is calm"))
                .Add("angry", JsonValue.Create("The text is angry")),
        }),
        Assets = images
            ? ImmutableDictionary<string, DecisionImage>.Empty.Add("img", new DecisionImage { Format = "png", DataBase64 = "AAAA" })
            : null,
        LongContext = longContext,
    };

    private static TypeSafeDecisionModel Create(
        TestHttpMessageHandler handler,
        TypeSafeOptions? options = null,
        ILlmUsageSink? sink = null,
        IAsyncPolicy<HttpResponseMessage>? policy = null,
        LlmKeyEnvironment? keys = null)
    {
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(TypeSafeDecisionModel), new HttpClient(handler));
        return new TypeSafeDecisionModel(
            factory,
            Options.Create(options ?? new TypeSafeOptions { ApiKey = Key }),
            NullLogger<TypeSafeDecisionModel>.Instance,
            sink,
            policy ?? Policy.NoOpAsync<HttpResponseMessage>(),
            keys ?? NoKeys);
    }

    [Fact]
    public async Task ShouldPostToSystemOneWithBearerAndDefaultModel_WhenDecideAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.CapturedRequests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", sent.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(Key, sent.Headers.Authorization.Parameter);
        Assert.False(sent.Headers.Contains(TypeSafeDecisionModel.ImagesHeader));
        Assert.False(sent.Headers.Contains(TypeSafeDecisionModel.LongContextHeader));
        var body = JsonNode.Parse(await sent.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("jev-latest", (string?)body["model"]);
        Assert.Equal("choice", (string?)body["questions"]!["tone"]!["type"]);
        Assert.Null(body["assets"]);
        Assert.Null(body["long_context"]);
    }

    [Fact]
    public async Task ShouldKeepTheRequestModel_WhenOneIsNamed()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler);

        await model.DecideAsync(ChoiceRequest() with { Model = "jev-1.14.0" }, TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(await handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("jev-1.14.0", (string?)body["model"]);
    }

    [Fact]
    public async Task ShouldSendTheDangerZoneHeaders_WhenImagesAndLongContext()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler);

        await model.DecideAsync(ChoiceRequest(images: true, longContext: true), TestContext.Current.CancellationToken);

        var sent = handler.CapturedRequests[0];
        Assert.Equal("true", Assert.Single(sent.Headers.GetValues(TypeSafeDecisionModel.ImagesHeader)));
        Assert.Equal("true", Assert.Single(sent.Headers.GetValues(TypeSafeDecisionModel.LongContextHeader)));
        var body = JsonNode.Parse(await sent.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("AAAA", (string?)body["assets"]!["img"]!["data_base64"]);
    }

    [Fact]
    public async Task ShouldReturnTypedAnswers_WhenTheApiAnswers()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler);

        var response = await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("jev-1.14.0", response.Model);
        var answer = response.Get<ChoiceAnswer>("tone");
        Assert.Equal("calm", answer.Choice);
        Assert.Equal(0.91, answer.Confidence, 3);
        Assert.Equal(42, response.Usage.InputTokens);
    }

    [Fact]
    public async Task ShouldMeterTheInputTokensAsADecision_WhenASinkIsGiven()
    {
        var sink = new MockLlmUsageSink();
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, sink: sink);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(LlmUsageOperations.Decision, usage.OperationType);
        Assert.Equal("typesafe", usage.Provider);
        Assert.Equal("jev-1.14.0", usage.Model);
        Assert.Equal(42, usage.PromptTokens);
        Assert.Equal(0, usage.CompletionTokens);
    }

    [Fact]
    public async Task ShouldRefuseBeforeSending_WhenTheRequestIsInvalid()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler);
        var invalid = ChoiceRequest() with { Questions = ImmutableDictionary<string, DecisionQuestion>.Empty };

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(invalid, TestContext.Current.CancellationToken));

        Assert.False(ex.IsTransient);
        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task ShouldNameTheVariableWithoutSending_WhenNoKeyIsSet()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(
            handler,
            options: new TypeSafeOptions());

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("TYPESAFE_API_KEY", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task ShouldReadTheKeyFromTheVariable_WhenTheOptionsHoldNone()
    {
        var keys = new LlmKeyEnvironment(_ => null, name => name == "MY_TS_KEY" ? "from-user-scope" : null);
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(
            handler,
            options: new TypeSafeOptions { ApiKeyEnvVar = "MY_TS_KEY" },
            keys: keys);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("from-user-scope", handler.CapturedRequests[0].Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(413, false)]
    [InlineData(415, false)]
    [InlineData(422, false)]
    [InlineData(429, true)]
    [InlineData(529, true)]
    public async Task ShouldThrowATypedError_WhenTheApiRefuses(int status, bool transient)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse((HttpStatusCode)status, """{"detail":"nope"}""");
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(transient, ex.IsTransient);
        Assert.DoesNotContain(Key, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldCiteTheFaultyField_WhenTheApiAnswers422()
    {
        const string body = """
            {"detail":[{"loc":["body","questions","tone","criteria"],"msg":"too many options","type":"value_error"}]}
            """;
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.UnprocessableEntity, body);
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("body.questions.tone.criteria: too many options", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameTheVariable_WhenTheApiRefusesTheKey()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "{}");
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("TYPESAFE_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRetry_WhenTheApiIsOverloaded()
    {
        var calls = 0;
        using var handler = new TestHttpMessageHandler(_ => ++calls == 1
            ? new HttpResponseMessage((HttpStatusCode)529) { Content = new StringContent("{}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ChoiceBody) });
        var policy = ResiliencePolicies.GetLlmApiPolicy(maxRetryAttempts: 2, delay: (_, _) => System.Threading.Tasks.Task.CompletedTask);
        var model = Create(handler, policy: policy);

        var response = await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("calm", response.Get<ChoiceAnswer>("tone").Choice);
        Assert.Equal(2, handler.CapturedRequests.Count);
    }

    [Fact]
    public async Task ShouldWrapANetworkError_AsTransient()
    {
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("refused"));
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.True(ex.IsTransient);
        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public async Task ShouldThrow_WhenTheBodyIsUnreadable()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "<html>");
        var model = Create(handler);

        await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldGetTheModels_WhenListModelsAsync()
    {
        const string body = """{"models":[{"name":"jev-1.14.0","description":"Jev","release_date":"2026-09-01"}]}""";
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var model = Create(handler);

        var models = await model.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("jev-1.14.0", Assert.Single(models).Name);
        Assert.Equal(HttpMethod.Get, handler.CapturedRequests[0].Method);
        Assert.Equal("https://api.typesafe.ai/v1/models", handler.CapturedRequests[0].RequestUri!.ToString());
    }

    [Fact]
    public void ShouldRegisterTheDecisionModel_WhenAddOrkeonTypeSafe()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "jev-1.14.0" })
            .Build();
        var services = new ServiceCollection().AddLogging().AddOrkeonTypeSafe(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<TypeSafeDecisionModel>(provider.GetRequiredService<IDecisionModel>());
        Assert.Equal("jev-1.14.0", provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value.Model);
    }

    [Fact]
    [Trait("BR", "BR-09")]
    public async Task ShouldNameLimitInFailure_WhenModelRefusesStateAsTooLong()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.BadRequest, """{"detail":"max_tokens_exceeded"}""");
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("64000", ex.Message, StringComparison.Ordinal);
        Assert.Contains("long context", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "BR-09")]
    public async Task ShouldNameLongContextLimit_WhenLongRequestRefusedAsTooLong()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.BadRequest, """{"detail":"max_tokens_exceeded"}""");
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(longContext: true), TestContext.Current.CancellationToken));

        Assert.Contains("262000", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("64000", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("set long context", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("BR", "BR-10")]
    [InlineData(400, """{"detail":"max_tokens_exceeded"}""")]
    [InlineData(401, "{}")]
    public async Task ShouldNotLeakKey_WhenFailureMessageBuilt(int status, string body)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse((HttpStatusCode)status, body);
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Key, ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"model":"jev-1.14.0","answers":null,"usage":{"input_tokens":1,"output_tokens":0}}""")]
    [InlineData("""{"model":"jev-1.14.0","answers":{},"usage":null}""")]
    [InlineData("""{"model":"","answers":{},"usage":{"input_tokens":1,"output_tokens":0}}""")]
    public async Task ShouldThrowANonTransientError_WhenTheBodyLacksAnswersUsageOrModel(string body)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("TypeSafe returned a body without answers, usage or model on /systemone.", ex.Message);
        Assert.False(ex.IsTransient);
        Assert.Null(ex.StatusCode);
    }

    [Theory]
    [InlineData("Orkeon:TypeSafe:MaxRetries", "-1", "Orkeon:TypeSafe:MaxRetries")]
    [InlineData("Orkeon:TypeSafe:TimeoutSeconds", "0", "Orkeon:TypeSafe:TimeoutSeconds")]
    [InlineData("Orkeon:TypeSafe:BaseUrl", "api/v1", "Orkeon:TypeSafe:BaseUrl")]
    [InlineData("Orkeon:TypeSafe:Guard:BlockThreshold", "1.5", "Orkeon:TypeSafe:Guard:BlockThreshold")]
    [InlineData("Orkeon:TypeSafe:Guard:WarnThreshold", "-0.1", "WarnThreshold")]
    public void ShouldRefuseTheOptionsNamingTheKey_WhenAValueIsOutOfRange(string key, string value, string named)
    {
        using var provider = BuildTypeSafe(new Dictionary<string, string?> { [key] = value });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value);

        Assert.Contains(named, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldValidateTheOptions_WhenTheSectionIsWellFormed()
    {
        using var provider = BuildTypeSafe(new Dictionary<string, string?>
        {
            ["Orkeon:TypeSafe:Model"] = "jev-1.14.0",
            ["Orkeon:TypeSafe:BaseUrl"] = "https://typesafe.example.test/v1/",
            ["Orkeon:TypeSafe:TimeoutSeconds"] = "10",
            ["Orkeon:TypeSafe:MaxRetries"] = "0",
            ["Orkeon:TypeSafe:Guard:BlockThreshold"] = "1",
            ["Orkeon:TypeSafe:Guard:WarnThreshold"] = "0",
        });

        var options = provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        Assert.Equal(0, options.MaxRetries);
        Assert.Equal(new Uri("https://typesafe.example.test/v1/"), options.BaseUrl);
        Assert.Equal(1, options.Guard.BlockThreshold);
    }

    [Fact]
    public void ShouldRegisterTheSystemOneDecideToolOnce_WhenAddOrkeonTypeSafeIsCalledTwice()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "jev-1.14.0" })
            .Build();
        var services = new ServiceCollection().AddOrkeonTypeSafe(configuration).AddOrkeonTypeSafe(configuration);

        var tool = Assert.Single(services, d => d.ServiceType == typeof(Orkeon.Domain.Tools.IBaseTool)
            && d.ImplementationType == typeof(Orkeon.Infrastructure.Tools.SystemOneDecideTool));
        Assert.Equal(ServiceLifetime.Singleton, tool.Lifetime);
        Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
    }

    private static ServiceProvider BuildTypeSafe(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddOrkeonTypeSafe(configuration).BuildServiceProvider();
    }
}
