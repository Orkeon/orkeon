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
using Orkeon.Infrastructure.Decision.SystemOne;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Resilience;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;

namespace Orkeon.Infrastructure.Tests.Decision.SystemOne;

public class SystemOneDecisionModelTests
{
    private const string Key = "ts-test-key";

    private const string ChoiceBody = """
        {"model":"jev-1.14.0","answers":{"tone":{"type":"choice","choice":"calm","confidence":0.91,
        "probabilities":{"calm":0.91,"angry":0.09}}},"usage":{"input_tokens":42,"output_tokens":0}}
        """;

    private static readonly LlmKeyEnvironment NoKeys = new(_ => null, _ => null);

    private static DecisionProviderDescriptor Provider(string key) => key switch
    {
        "typesafe" => DecisionProviderDescriptor.TypeSafe,
        "liquid" => DecisionProviderDescriptor.Liquid,
        "kev" => DecisionProviderDescriptor.Kev,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

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

    private static SystemOneDecisionModel Create(
        TestHttpMessageHandler handler,
        string provider = "typesafe",
        Action<SystemOneProviderOptions>? configure = null,
        ILlmUsageSink? sink = null,
        IAsyncPolicy<HttpResponseMessage>? policy = null,
        LlmKeyEnvironment? keys = null)
    {
        var descriptor = Provider(provider);
        var options = new SystemOneProviderOptions { ApiKey = Key };
        descriptor.ApplyDefaults(options);
        configure?.Invoke(options);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(SystemOneDecisionModel), new HttpClient(handler));
        return new SystemOneDecisionModel(
            descriptor,
            factory,
            Options.Create(options),
            NullLogger<SystemOneDecisionModel>.Instance,
            sink,
            policy ?? Policy.NoOpAsync<HttpResponseMessage>(),
            keys ?? NoKeys);
    }

    [Theory]
    [Trait("BR", "BR-12")]
    [Trait("BR", "UC-03")]
    [InlineData("typesafe", "https://api.typesafe.ai/v1/systemone", "jev-latest")]
    [InlineData("liquid", "https://api.liquid.ai/decisions/v1/systemone", "d1.1")]
    [InlineData("kev", "http://127.0.0.1:8009/v1/systemone", "kev-latest")]
    public async Task ShouldPostToTheProviderAddressWithItsDefaultModel_WhenDecideAsked(string provider, string url, string defaultModel)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, provider);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.CapturedRequests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(url, sent.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(Key, sent.Headers.Authorization.Parameter);
        Assert.False(sent.Headers.Contains(SystemOneDecisionModel.ImagesHeader));
        Assert.False(sent.Headers.Contains(SystemOneDecisionModel.LongContextHeader));
        var body = JsonNode.Parse(await sent.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(defaultModel, (string?)body["model"]);
        Assert.Equal("choice", (string?)body["questions"]!["tone"]!["type"]);
        Assert.Null(body["assets"]);
        Assert.Null(body["long_context"]);
    }

    [Theory]
    [Trait("BR", "BR-13")]
    [InlineData("typesafe")]
    [InlineData("liquid")]
    [InlineData("kev")]
    public async Task ShouldKeepTheRequestModel_WhenOneIsNamed(string provider)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, provider);

        await model.DecideAsync(ChoiceRequest() with { Model = "jev-1.14.0" }, TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(await handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("jev-1.14.0", (string?)body["model"]);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public async Task ShouldSendNoAuthorization_WhenKevHasNoKey()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, "kev", o => o.ApiKey = null);

        var response = await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("calm", response.Get<ChoiceAnswer>("tone").Choice);
        var sent = Assert.Single(handler.CapturedRequests);
        Assert.Null(sent.Headers.Authorization);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public async Task ShouldSendTheBearer_WhenKevHasAKey()
    {
        var keys = new LlmKeyEnvironment(name => name == "KEV_API_KEY" ? "kev-secret" : null, _ => null);
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, "kev", o => o.ApiKey = null, keys: keys);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.CapturedRequests);
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal("kev-secret", sent.Headers.Authorization.Parameter);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public void ShouldKeepTypeSafeDefaults_WhenTheSectionIsEmpty()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:TypeSafe"] = null }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SystemOneProviderOptions>>().Value;

        Assert.Equal("jev-latest", options.Model);
        Assert.Equal(new Uri("https://api.typesafe.ai/v1"), options.BaseUrl);
        Assert.Equal("TYPESAFE_API_KEY", options.ApiKeyEnvVar);
    }

    [Fact]
    public async Task ShouldSendTheDangerZoneHeaders_WhenImagesAndLongContext()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler);

        await model.DecideAsync(ChoiceRequest(images: true, longContext: true), TestContext.Current.CancellationToken);

        var sent = handler.CapturedRequests[0];
        Assert.Equal("true", Assert.Single(sent.Headers.GetValues(SystemOneDecisionModel.ImagesHeader)));
        Assert.Equal("true", Assert.Single(sent.Headers.GetValues(SystemOneDecisionModel.LongContextHeader)));
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

    [Theory]
    [Trait("BR", "BR-11")]
    [InlineData("typesafe")]
    [InlineData("liquid")]
    [InlineData("kev")]
    public async Task ShouldMeterTheTokensAgainstTheProvider_WhenItAnswers(string provider)
    {
        var sink = new MockLlmUsageSink();
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, provider, sink: sink);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(LlmUsageOperations.Decision, usage.OperationType);
        Assert.Equal(provider, usage.Provider);
        Assert.Equal("jev-1.14.0", usage.Model);
        Assert.Equal(42, usage.PromptTokens);
        Assert.Equal(0, usage.CompletionTokens);
    }

    [Fact]
    public async Task ShouldMeterZeroTokens_WhenTheUsageCarriesNullCounts()
    {
        var sink = new MockLlmUsageSink();
        const string body = """
            {"model":"jev-1.14.0","answers":{"tone":{"type":"choice","choice":"calm","confidence":0.91,
            "probabilities":{"calm":0.91,"angry":0.09}}},"usage":{"input_tokens":null,"output_tokens":null}}
            """;
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var model = Create(handler, sink: sink);

        var response = await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        Assert.Null(response.Usage.InputTokens);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(0, usage.PromptTokens);
        Assert.Equal(0, usage.CompletionTokens);
    }

    [Fact]
    public async Task ShouldCiteTheRequestId_WhenTheApiRefusesWithOne()
    {
        using var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"detail":"state is not valid"}""", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.Add(SystemOneDecisionModel.RequestIdHeader, "req_0123abcd");
            return response;
        });
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("state is not valid", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith("(TypeSafe request req_0123abcd)", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, ex.Message, StringComparison.Ordinal);
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

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData("typesafe", "TYPESAFE_API_KEY", "Orkeon:TypeSafe:ApiKey")]
    [InlineData("liquid", "LIQUID_API_KEY", "Orkeon:Liquid:ApiKey")]
    public async Task ShouldNameTheProviderVariableWithoutSending_WhenAHostedProviderHasNoKey(string provider, string variable, string setting)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, provider, o => o.ApiKey = null);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains(provider, ex.Message, StringComparison.Ordinal);
        Assert.Contains(variable, ex.Message, StringComparison.Ordinal);
        Assert.Contains(setting, ex.Message, StringComparison.Ordinal);
        Assert.False(ex.IsTransient);
        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task ShouldReadTheKeyFromTheVariable_WhenTheOptionsHoldNone()
    {
        var keys = new LlmKeyEnvironment(_ => null, name => name == "MY_TS_KEY" ? "from-user-scope" : null);
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = Create(handler, configure: o => { o.ApiKey = null; o.ApiKeyEnvVar = "MY_TS_KEY"; }, keys: keys);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("from-user-scope", handler.CapturedRequests[0].Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(400, false)]
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

    [Theory]
    [Trait("BR", "BR-07")]
    [Trait("BR", "BR-08")]
    [InlineData("typesafe", 401, "typesafe", "TYPESAFE_API_KEY", "Orkeon:TypeSafe")]
    [InlineData("typesafe", 403, "typesafe", "TYPESAFE_API_KEY", "Orkeon:TypeSafe")]
    [InlineData("liquid", 401, "liquid", "LIQUID_API_KEY", "Orkeon:Liquid")]
    [InlineData("liquid", 403, "liquid", "LIQUID_API_KEY", "Orkeon:Liquid")]
    [InlineData("kev", 401, "kev", "KEV_API_KEY", "Orkeon:Kev")]
    [InlineData("kev", 403, "kev", "KEV_API_KEY", "Orkeon:Kev")]
    public async Task ShouldNameTheVariable_WhenTheApiRefusesTheKey(string provider, int status, string name, string variable, string section)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse((HttpStatusCode)status, "{}");
        var model = Create(handler, provider);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Equal($"{name} refused the key: set {variable} or {section}:ApiKey.", ex.Message);
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
    [Trait("BR", "BR-09")]
    public async Task ShouldNameTheProviderAndAddress_WhenTheProviderCannotBeReached()
    {
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("refused"));
        var policy = ResiliencePolicies.GetLlmApiPolicy(maxRetryAttempts: 2, delay: (_, _) => System.Threading.Tasks.Task.CompletedTask);
        var model = Create(handler, "kev", policy: policy);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.True(ex.IsTransient);
        Assert.Null(ex.StatusCode);
        Assert.Contains("kev", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://127.0.0.1:8009/v1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-09")]
    public async Task ShouldNameTheProviderAndAddress_WhenEveryAttemptTimesOut()
    {
        using var handler = TestHttpMessageHandler.CreateWithException(new TaskCanceledException("timed out"));
        var policy = ResiliencePolicies.GetLlmApiPolicy(maxRetryAttempts: 2, delay: (_, _) => System.Threading.Tasks.Task.CompletedTask);
        var model = Create(handler, "kev", policy: policy);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.True(ex.IsTransient);
        Assert.Contains("kev", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://127.0.0.1:8009/v1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-10")]
    public async Task ShouldQuoteTheProviderWithoutRetry_WhenItRefusesAQuestionType()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.BadRequest, """{"detail":"question type not supported: number"}""");
        var policy = ResiliencePolicies.GetLlmApiPolicy(maxRetryAttempts: 2, delay: (_, _) => System.Threading.Tasks.Task.CompletedTask);
        var model = Create(handler, "kev", policy: policy);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Single(handler.CapturedRequests);
        Assert.False(ex.IsTransient);
        Assert.Equal("kev refused the request: question type not supported: number", ex.Message);
    }

    [Fact]
    public async Task ShouldThrow_WhenTheBodyIsUnreadable()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "<html>");
        var model = Create(handler);

        await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ShouldRegisterTheDecisionModel_WhenAddOrkeonDecisionProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "jev-1.14.0" })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<SystemOneDecisionModel>(provider.GetRequiredService<IDecisionModel>());
        Assert.Equal("jev-1.14.0", provider.GetRequiredService<IOptions<SystemOneProviderOptions>>().Value.Model);
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
    public void ShouldRefuseTheOptionsNamingTheKey_WhenAValueIsOutOfRange(string key, string value, string named)
    {
        using var provider = BuildTypeSafe(new Dictionary<string, string?> { [key] = value });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SystemOneProviderOptions>>().Value);

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
        });

        var options = provider.GetRequiredService<IOptions<SystemOneProviderOptions>>().Value;

        Assert.Equal(0, options.MaxRetries);
        Assert.Equal(new Uri("https://typesafe.example.test/v1/"), options.BaseUrl);
    }

    [Fact]
    public void ShouldRegisterTheSystemOneDecideToolOnce_WhenAddOrkeonDecisionProviderIsCalledTwice()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "jev-1.14.0" })
            .Build();
        var services = new ServiceCollection();
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);

        var tool = Assert.Single(services, d => d.ServiceType == typeof(Orkeon.Domain.Tools.IBaseTool)
            && d.ImplementationType == typeof(Orkeon.Infrastructure.Tools.SystemOneDecideTool));
        Assert.Equal(ServiceLifetime.Singleton, tool.Lifetime);
        Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
    }

    [Fact]
    public async Task ShouldNameLimitInFailure_WhenModelRefusesStateAsTooLong()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.BadRequest, """{"detail":"max_tokens_exceeded"}""");
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("64000", ex.Message, StringComparison.Ordinal);
        Assert.Contains("long context", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldNameLongContextLimit_WhenLongRequestRefusedAsTooLong()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.BadRequest, """{"detail":"max_tokens_exceeded"}""");
        var model = Create(handler);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(longContext: true), TestContext.Current.CancellationToken));

        Assert.Contains("262000", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("64000", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("set long context", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> BlankValues(string section) => new()
    {
        [$"{section}:Model"] = "",
        [$"{section}:ApiKeyEnvVar"] = "",
        [$"{section}:BaseUrl"] = "",
    };

    private static SystemOneProviderOptions BoundOptions(DecisionProviderDescriptor descriptor)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(BlankValues(descriptor.Section)).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddOrkeonDecisionProvider(configuration, descriptor);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SystemOneProviderOptions>>().Value;
    }

    private static SystemOneDecisionModel CreateFrom(TestHttpMessageHandler handler, DecisionProviderDescriptor descriptor, SystemOneProviderOptions options)
    {
        var factory = new TestHttpClientFactory();
        factory.RegisterClient(nameof(SystemOneDecisionModel), new HttpClient(handler));
        return new SystemOneDecisionModel(
            descriptor,
            factory,
            Options.Create(options),
            NullLogger<SystemOneDecisionModel>.Instance,
            null,
            Policy.NoOpAsync<HttpResponseMessage>(),
            NoKeys);
    }

    [Theory]
    [Trait("BR", "BR-12")]
    [InlineData("typesafe", "https://api.typesafe.ai/v1", "jev-latest", "TYPESAFE_API_KEY")]
    [InlineData("liquid", "https://api.liquid.ai/decisions/v1", "d1.1", "LIQUID_API_KEY")]
    [InlineData("kev", "http://127.0.0.1:8009/v1", "kev-latest", "KEV_API_KEY")]
    public void ShouldKeepProviderDefaults_WhenTheSectionBindsBlankValues(string provider, string url, string defaultModel, string variable)
    {
        var options = BoundOptions(Provider(provider));

        Assert.Equal(defaultModel, options.Model);
        Assert.Equal(variable, options.ApiKeyEnvVar);
        Assert.Equal(new Uri(url), options.BaseUrl);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public async Task ShouldPostTheDefaultModel_WhenTheBoundModelIsBlank()
    {
        var options = BoundOptions(DecisionProviderDescriptor.TypeSafe);
        options.ApiKey = Key;
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ChoiceBody);
        var model = CreateFrom(handler, DecisionProviderDescriptor.TypeSafe, options);

        await model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(await handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("jev-latest", (string?)body["model"]);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldNameTheDefaultVariable_WhenTheBoundVariableIsBlankAndTheApiRefusesTheKey()
    {
        var options = BoundOptions(DecisionProviderDescriptor.TypeSafe);
        options.ApiKey = Key;
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "{}");
        var model = CreateFrom(handler, DecisionProviderDescriptor.TypeSafe, options);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("typesafe refused the key: set TYPESAFE_API_KEY or Orkeon:TypeSafe:ApiKey.", ex.Message);
    }

    [Theory]
    [Trait("BR", "BR-07")]
    [Trait("BR", "BR-08")]
    [InlineData("typesafe", 400, """{"detail":"max_tokens_exceeded"}""")]
    [InlineData("typesafe", 401, "{}")]
    [InlineData("liquid", 400, """{"detail":"max_tokens_exceeded"}""")]
    [InlineData("liquid", 401, "{}")]
    [InlineData("kev", 400, """{"detail":"max_tokens_exceeded"}""")]
    [InlineData("kev", 401, "{}")]
    public async Task ShouldNotLeakKey_WhenFailureMessageBuilt(string provider, int status, string body)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse((HttpStatusCode)status, body);
        var model = Create(handler, provider);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(ChoiceRequest(), TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Key, ex.ToString(), StringComparison.Ordinal);
    }

    private static ServiceProvider BuildTypeSafe(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);
        return services.BuildServiceProvider();
    }
}
