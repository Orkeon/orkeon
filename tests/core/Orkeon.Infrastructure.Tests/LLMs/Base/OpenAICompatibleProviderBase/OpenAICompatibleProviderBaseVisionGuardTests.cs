using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs.Base;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>A provider that declares no vision — the silent-drop case.</summary>
public sealed class TextOnlyProvider : OpenAICompatibleProviderBase
{
    public override string Name => "TextOnlyProvider";
    protected override Uri DefaultBaseUrl => new("https://api.test.com/v1");
    protected override string DefaultModel => TestModelName;
    protected override string ProviderDisplayName => "TextOnly";

    public override LlmProviderCapabilities Capabilities { get; } = new() { Vision = false };

    public TextOnlyProvider(
        LlmConfig config, IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy, ILogger<TextOnlyProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }
}

/// <summary>A provider that declares vision — the per-model trap.</summary>
public sealed class VisionClaimingProvider : OpenAICompatibleProviderBase
{
    public override string Name => "VisionClaimingProvider";
    protected override Uri DefaultBaseUrl => new("https://api.test.com/v1");
    protected override string DefaultModel => TestModelName;
    protected override string ProviderDisplayName => "VisionClaiming";

    public override LlmProviderCapabilities Capabilities { get; } = new() { Vision = true };

    public VisionClaimingProvider(
        LlmConfig config, IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy, ILogger<VisionClaimingProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }
}

/// <summary>
/// What happens to an image a provider or a model cannot accept (D-03).
/// </summary>
/// <remarks>
/// <para>
/// Two failure shapes, found by the Z.AI campaign of 2026-08-01. A provider that declares no
/// vision never takes the structured content path, so the image was flattened away and the
/// model answered about a picture it never saw — a silent drop. A provider that declares
/// vision hands the image to a model that may be text-only, and the vendor answers with a 400
/// that is accurate but says nothing about why Orkeon sent an image at all.
/// </para>
/// <para>
/// Neither is fixable by knowing more: vision is a property of the model, and Orkeon holds no
/// per-model table. What is fixable is the silence — which is what these tests pin.
/// </para>
/// </remarks>
public class OpenAICompatibleProviderBaseVisionGuardTests
{
    private const string TinyPng =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAIAAAAlC+aJAAAAS0lEQVR42u3PQQkAAAgAsetfWiP4Fg" +
        "YrsKZeS0BAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEDgsqnc8OJg" +
        "6Ln3AAAAAElFTkSuQmCC";

    /// <summary>The refusal Z.AI really returned for `glm-5.2` on 2026-08-01.</summary>
    private const string ZaiVisionRefusal =
        """{"error":{"code":"1210","message":"messages.content.type is invalid, allowed values: ['text']"}}""";

    private static readonly string OkResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1 },
    });

    private readonly TestDoubles.TestHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static LlmMessage ImageMessage() => LlmMessage.User(
        MultiModalContent.FromText("What colour is this?")
            .AddImage(ImageContentPart.FromBase64(TinyPng, "image/png")));

    // ── A provider with no vision: the drop must be reported ────────────────

    [Fact]
    public async Task ShouldWarn_WhenAnImageIsSentToAProviderThatDeclaresNoVision()
    {
        var logger = new TestDoubles.TestLogger<TextOnlyProvider>();
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse);
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TextOnlyProvider", httpClient);

        using var provider = new TextOnlyProvider(
            LlmConfig.Create(TestModelName, TestApiKey), _httpClientFactory, _noOpPolicy, logger);

        await provider.ChatAsync([ImageMessage()], cancellationToken: TestContext.Current.CancellationToken);

        var warning = Assert.Single(logger.LoggedMessages, m => m.Contains("attachments", StringComparison.Ordinal));
        Assert.Contains("Image", warning, StringComparison.Ordinal);
        Assert.Contains("TextOnly", warning, StringComparison.Ordinal);
        // The remedy matters as much as the diagnosis: a warning with no way forward is noise.
        Assert.Contains("only the text parts were sent", warning, StringComparison.Ordinal);
    }

    /// <summary>A text-only conversation must not produce a warning about attachments.</summary>
    [Fact]
    public async Task ShouldStaySilent_WhenTheConversationCarriesOnlyText()
    {
        var logger = new TestDoubles.TestLogger<TextOnlyProvider>();
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse);
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TextOnlyProvider", httpClient);

        using var provider = new TextOnlyProvider(
            LlmConfig.Create(TestModelName, TestApiKey), _httpClientFactory, _noOpPolicy, logger);

        await provider.ChatAsync([LlmMessage.User("Hello.")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.LoggedMessages, m => m.Contains("attachments", StringComparison.Ordinal));
    }

    /// <summary>
    /// A provider that can actually send the image has nothing to warn about — warning there
    /// would train readers to ignore the message.
    /// </summary>
    [Fact]
    public async Task ShouldStaySilent_WhenTheProviderCanSendTheImage()
    {
        var logger = new TestDoubles.TestLogger<VisionClaimingProvider>();
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse);
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("VisionClaimingProvider", httpClient);

        using var provider = new VisionClaimingProvider(
            LlmConfig.Create(TestModelName, TestApiKey), _httpClientFactory, _noOpPolicy, logger);

        await provider.ChatAsync([ImageMessage()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.LoggedMessages, m => m.Contains("attachments", StringComparison.Ordinal));
    }

    // ── A provider that claims vision: the 400 must name the trap ───────────

    [Fact]
    public async Task ShouldNameThePerModelVisionTrap_WhenTheApiRejectsTheImage()
    {
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.BadRequest, ZaiVisionRefusal);
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("VisionClaimingProvider", httpClient);

        var config = LlmConfig.Create("glm-5.2", TestApiKey);
        using var provider = new VisionClaimingProvider(config, _httpClientFactory, _noOpPolicy);

        var result = await provider.ChatAsync(
            [ImageMessage()], cancellationToken: TestContext.Current.CancellationToken);

        var error = result.Metadata["error"].ToString() ?? "";
        // The vendor's own words are kept — they are the ground truth.
        Assert.Contains("allowed values: ['text']", error, StringComparison.Ordinal);
        // …and the hint names the cause the vendor cannot know about.
        Assert.Contains("declared per provider while models differ", error, StringComparison.Ordinal);
        Assert.Contains("glm-5.2", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hint is only ever right when an image was actually sent. Appending it to every 400
    /// would send readers hunting for a vision problem that does not exist.
    /// </summary>
    [Fact]
    public async Task ShouldNotMentionVision_WhenATextOnlyRequestFails()
    {
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.BadRequest, """{"error":{"message":"rate limit exceeded"}}""");
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("VisionClaimingProvider", httpClient);

        // Tools, not an image, select the native path — so the failure has nothing to do with vision.
        var config = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            Tools = [new Domain.Tools.Protocol.ToolSchema("t", "d", [])],
        };
        using var provider = new VisionClaimingProvider(config, _httpClientFactory, _noOpPolicy);

        var result = await provider.ChatAsync(
            [LlmMessage.User("Hello.")], cancellationToken: TestContext.Current.CancellationToken);

        var error = result.Metadata["error"].ToString() ?? "";
        Assert.Contains("rate limit exceeded", error, StringComparison.Ordinal);
        // Asserted on the hint's own wording, not on the word "vision": the provider's display
        // name legitimately contains it, and a substring match there would pass for the wrong reason.
        Assert.DoesNotContain("declared per provider while models differ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("may be text-only", error, StringComparison.Ordinal);
    }
}
