using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Validation;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.LLMs.Replay;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>What a vendor is sent of a run's images: within its own limits, and once more without them when it refuses.</summary>
public sealed class RunImageLlmProviderTests
{
    private const string Photo = "/inbox/photo.png";
    private const string Small = "/inbox/small.png";
    private const string Notes = "/inbox/notes.txt";
    private const string CassetteFile = "/llm-cassette/run.jsonl";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeFileSystemService _files = new FakeFileSystemService().AddMount("/inbox")
        .AddMount("/llm-cassette", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create)
        .AddFile(Photo, Png(20, 20)).AddFile(Small, Png(30, 30));

    private readonly MultiModalOptions _options = new();

    /// <summary>A vendor that refuses the calls carrying an image, the way <see cref="Refuse"/> says.</summary>
    private sealed class StubVendor : ILlmProvider, IStreamingLlmProvider
    {
        public string Name { get; init; } = "openrouter";

        public LlmConfig? BaseConfig { get; } = LlmConfig.Create("gpt-4o");

        public LlmProviderCapabilities Capabilities { get; } = LlmProviderCapabilities.Unknown with { Vision = true };

        public List<LlmMessage[]> Calls { get; } = [];

        public Func<LlmResponse>? Refuse { get; set; }

        public bool RefusesEverything { get; set; }

        public bool SpeaksBeforeItFails { get; set; }

        public bool SupportsStreaming => true;

        public bool StreamsNatively => true;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse { Content = prompt });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(messages);
            return Task.FromResult(Refuse is not null && (RefusesEverything || ImagesOf(messages).Count > 0)
                ? Refuse() : new LlmResponse { Content = "seen", TokensUsed = 2 });
        }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return prompt;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (SpeaksBeforeItFails)
                yield return LlmStreamEvent.Content("par");
            var response = await ChatAsync(messages, config, cancellationToken);
            if (response.Content.Length > 0)
                yield return LlmStreamEvent.Content(response.Content);
            yield return LlmStreamEvent.Complete(response);
        }
    }

    private static byte[] Png(uint width, uint height, int size = 64)
    {
        var bytes = Padded([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8], size);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Padded(byte[] head, int size = 64)
    {
        var bytes = new byte[size];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private static List<ImageContentPart> ImagesOf(LlmMessage[] messages) =>
        [.. messages.Where(m => m.MultiModalContent is not null).SelectMany(m => m.MultiModalContent!.Parts).OfType<ImageContentPart>()];

    private static LlmResponse Error(string error, string? type = "APIError", int? status = null)
    {
        var metadata = new Dictionary<string, object> { ["error"] = error };
        if (type is not null)
            metadata["error_type"] = type;
        if (status is { } code)
            metadata["http_status_code"] = code;
        return new LlmResponse { Content = "", Metadata = metadata };
    }

    private RunImageLlmProvider Over(StubVendor vendor) => new(vendor, _files, _options, new ModelCapabilityRegistry());

    private static async Task<(LlmResponse Response, IReadOnlyList<UnseenImage> Unseen, string Text)> AskAsync(
        ILlmProvider provider, bool streaming, params string[] images)
    {
        using var run = RunImages.Begin([.. images.Select(i => new CrewInputImage { Path = i })]);
        using var turn = RunImages.OpenTurn("Looker");
        LlmMessage[] messages = [new() { Role = "user", Content = "hello" }];
        if (!streaming)
            return (await provider.ChatAsync(messages, null, Ct), RunImages.Unseen, string.Empty);

        LlmResponse? final = null;
        var text = string.Empty;
        await foreach (var ev in ((IStreamingLlmProvider)provider).ChatStreamingAsync(messages, null, Ct))
        {
            final = ev.FinalResponse ?? final;
            text += ev.Delta;
        }

        return (final!, RunImages.Unseen, text);
    }

    [Theory]
    [InlineData("anthropic", 7_500_000)]
    [InlineData("azure-openai", 20_000_000)]
    [InlineData("grok", 20_971_520)]
    [InlineData("mistral", 20_000_000)]
    [InlineData("minimax", 10_000_000)]
    [InlineData("zai", 4_999_999)]
    [InlineData("openrouter", 3_750_000)]
    [InlineData("a provider of the host", 3_750_000)]
    public async Task ShouldLeaveOutAnImageOverItsProvidersSize_AndShowTheOthers(string name, int limit)
    {
        _options.MaxImageSizeBytes = 100_000_000;
        var vendor = new StubVendor { Name = name };
        _files.AddFile("/inbox/at.png", Png(20, 20, limit));
        Assert.Empty((await AskAsync(Over(vendor), false, "/inbox/at.png")).Unseen);

        _files.AddFile(Photo, Png(20, 20, limit + 1));
        var (response, unseen, _) = await AskAsync(Over(vendor), false, Photo, Small);

        Assert.Null(response.Error);
        Assert.Single(ImagesOf(vendor.Calls[^1]));
        var left = Assert.Single(unseen);
        Assert.Equal(Photo, left.Path);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"{limit} bytes"), left.Reason, StringComparison.Ordinal);
        Assert.Contains($"[Image not shown to you: \"{Photo}\"]", vendor.Calls[^1][0].Content, StringComparison.Ordinal);
    }

    // The request's limit, less a million bytes for the rest of it, counted in base64.
    [Theory]
    [InlineData("gemini", 14_250_000)]
    [InlineData("qwen", 11_832_912)]
    public async Task ShouldKeepTheImagesOfACallWithinItsProvidersRequest(string name, int room)
    {
        var vendor = new StubVendor { Name = name };
        _files.AddFile(Photo, Png(20, 20, room - 63));

        var (_, unseen, _) = await AskAsync(Over(vendor), false, Photo, Small);

        Assert.Single(ImagesOf(vendor.Calls[^1]));
        Assert.Contains("request", Assert.Single(unseen, u => u.Path == Small).Reason, StringComparison.Ordinal);
    }

    public static TheoryData<string, byte[], bool> Dimensions => new()
    {
        { "anthropic", Png(8000, 8000), true },
        { "anthropic", Png(8001, 10), false },
        { "zai", Png(10, 6001), false },
        { "qwen", Png(10, 100), false },
        { "qwen", Png(11, 100), true },
        { "minimax", Png(201, 1), false },
        { "minimax", Png(200, 1), true },
        { "minimax", Png(0xFFFFFFFF, 1), false },
        { "anthropic", Padded([.. "GIF89a"u8, 0x41, 0x1F, 10, 0]), false },
        { "anthropic", Padded([0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 1, 2, 0xFF, 0xFF, 0xC2, 0, 11, 8, 0x1F, 0x41, 0, 10]), false },
        { "anthropic", Padded([0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 1, 2, 0xFF, 0xC0, 0, 11, 8, 0x1F, 0x40, 0, 10]), true },
        { "anthropic", Padded([0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xFF]), true },
        { "anthropic", Padded([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]), true },
        { "anthropic", Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0, 0x40, 0x1F, 0, 9, 0, 0]), false },
        { "anthropic", Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8L"u8, 0, 0, 0, 0, 0x2F, 0x40, 0x1F, 0, 0]), false },
        { "anthropic", Padded([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0, 0, 0, 0, 0x9D, 1, 0x2A, 0x41, 0x1F, 10, 0]), false },
        { "grok", Padded([.. "GIF89a"u8, 10, 0, 10, 0]), false },
        { "gemini", Padded([.. "GIF89a"u8, 10, 0, 10, 0]), false },
        { "openrouter", Png(9000, 9000), true },
        { "anthropic", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], true },
    };

    [Theory]
    [MemberData(nameof(Dimensions))]
    public async Task ShouldReadTheDimensionsAndTheFormatItsProviderTakes(string name, byte[] image, bool shown)
    {
        var vendor = new StubVendor { Name = name };
        _files.AddFile(Photo, image);

        var (_, unseen, _) = await AskAsync(Over(vendor), false, Photo);

        Assert.Equal(shown ? 1 : 0, ImagesOf(vendor.Calls[^1]).Count);
        Assert.Equal(shown ? 0 : 1, unseen.Count);
    }

    private static LlmResponse RefusalOf(string kind) => kind switch
    {
        "status" => Error("cannot decode", null, 400),
        "label" => Error("Anthropic API error: invalid_request_error - Could not process image"),
        "throw" => throw new HttpRequestException("rejected", null, HttpStatusCode.BadRequest),
        _ => Error($"X API error: {kind} - Could not process image"),
    };

    [Theory]
    [InlineData("BadRequest", false)]
    [InlineData("BadRequest", true)]
    [InlineData("RequestEntityTooLarge", false)]
    [InlineData("RequestEntityTooLarge", true)]
    [InlineData("UnprocessableEntity", false)]
    [InlineData("status", false)]
    [InlineData("status", true)]
    [InlineData("label", false)]
    [InlineData("label", true)]
    [InlineData("throw", false)]
    [InlineData("throw", true)]
    public async Task ShouldAskOnceMoreWithoutTheImages_WhenTheVendorRefusesTheCall(string kind, bool streaming)
    {
        var vendor = new StubVendor { Refuse = () => RefusalOf(kind) };
        _files.AddFile(Notes, "text"u8.ToArray());

        var (response, unseen, text) = await AskAsync(Over(vendor), streaming, Photo, Small, Notes);

        Assert.Equal("seen", response.Content);
        Assert.Equal(2, response.TokensUsed);
        Assert.Equal(streaming ? "seen" : string.Empty, text);
        Assert.Equal([2, 0], vendor.Calls.Select(c => ImagesOf(c).Count));
        Assert.Equal(3, unseen.Count);
        Assert.All(unseen.Where(u => u.Path != Notes), u => Assert.Contains("refused", u.Reason, StringComparison.Ordinal));
        Assert.Contains("not a PNG", Assert.Single(unseen, u => u.Path == Notes).Reason, StringComparison.Ordinal);
        Assert.Contains($"[Image not shown to you: \"{Photo}\"]", vendor.Calls[1][0].Content, StringComparison.Ordinal);
        Assert.Contains($"[Image not shown to you: \"{Small}\"]", vendor.Calls[1][0].Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldNotAskAThirdTime_WhenTheVendorRefusesTheCallWithoutImagesToo(bool streaming)
    {
        var vendor = new StubVendor { Refuse = () => RefusalOf("BadRequest"), RefusesEverything = true };

        var (response, unseen, _) = await AskAsync(Over(vendor), streaming, Photo);

        Assert.NotNull(response.Error);
        Assert.Equal(2, vendor.Calls.Count);
        Assert.Empty(unseen);
    }

    [Theory]
    [InlineData("X API error: Unauthorized - no")]
    [InlineData("X API error: PaymentRequired - no")]
    [InlineData("X API error: Forbidden - no")]
    [InlineData("X API error: NotFound - no")]
    [InlineData("X API error: RequestTimeout - no")]
    [InlineData("X API error: TooManyRequests - no")]
    [InlineData("X API error: InternalServerError - no")]
    [InlineData("X API error: BadRequest - {\"code\":\"context_length_exceeded\"}")]
    [InlineData("X API error: BadRequest - This model's maximum context length is 8192 tokens")]
    [InlineData("Anthropic API error: BadRequest - prompt is too long: 210000 tokens > 200000 maximum")]
    [InlineData("Anthropic API error: BadRequest - Your credit balance is too low to access the API")]
    [InlineData("Gemini API error: BadRequest - API key not valid. Please pass a valid API key.")]
    [InlineData("a failure without a status")]
    public async Task ShouldLeaveAnyOtherErrorAsItIs(string error)
    {
        foreach (var streaming in (bool[])[false, true])
        {
            var vendor = new StubVendor { Refuse = () => Error(error) };

            var (response, unseen, _) = await AskAsync(Over(vendor), streaming, Photo);

            Assert.Equal(error, response.Error);
            Assert.Single(vendor.Calls);
            Assert.Empty(unseen);
        }
    }

    [Fact]
    public async Task ShouldAskOnce_WhenTheRefusedCallCarriedNoImage()
    {
        var vendor = new StubVendor { Refuse = () => RefusalOf("BadRequest"), RefusesEverything = true };
        _files.AddFile(Photo, "text"u8.ToArray());

        await AskAsync(Over(vendor), false, Photo);

        Assert.Single(vendor.Calls);
    }

    [Fact]
    public async Task ShouldNotAskAgain_WhenAStreamHasAlreadySpoken()
    {
        var vendor = new StubVendor { Refuse = () => RefusalOf("BadRequest"), SpeaksBeforeItFails = true };

        var (response, _, text) = await AskAsync(Over(vendor), true, Photo);

        Assert.NotNull(response.Error);
        Assert.Equal("par", text);
        Assert.Single(vendor.Calls);
    }

    [Theory]
    [InlineData("BadRequest", false)]
    [InlineData("BadRequest", true)]
    [InlineData("throw", false)]
    [InlineData("throw", true)]
    public async Task ShouldReplayARefusedCallAndItsSecond_WithoutTheVendor(string kind, bool streaming)
    {
        var vendor = new StubVendor { Refuse = () => RefusalOf(kind) };
        ILlmProvider Build(LlmReplayMode mode) => ((LlmProviderAdapter)new LlmProviderFactory(
            new NoHttpClientFactory(), NullLoggerFactory.Instance, _files, new LlmProviderFactoryOptions
            {
                Modules = [new StubModule(vendor)],
                Images = new RunImageSource(_files, _options),
                Replay = new LlmReplayOptions { Mode = mode, Cassette = CassetteFile },
            }).Create(Orkeon.Infrastructure.LLMs.Profiles.LlmSettings.ReadDefault(new ConfigurationBuilder()
                .AddInMemoryCollection([new("Llm:Provider", "stub"), new("Llm:Model", "gpt-4o")]).Build()))).UnderlyingProvider;

        await AskAsync(Build(LlmReplayMode.Record), streaming, Photo);
        Assert.Equal(2, vendor.Calls.Count);
        var lines = (await _files.TryReadAllTextAsync(CassetteFile, Ct))!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Count(l => l.Contains("\"key\"", StringComparison.OrdinalIgnoreCase)));

        var (response, unseen, _) = await AskAsync(Build(LlmReplayMode.Replay), streaming, Photo);

        Assert.Equal("seen", response.Content);
        Assert.Single(unseen);
        Assert.Equal(2, vendor.Calls.Count);
    }

    private sealed class StubModule(StubVendor vendor) : ILlmProviderModule
    {
        public string Key => "stub";

        public ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context) => vendor;
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
