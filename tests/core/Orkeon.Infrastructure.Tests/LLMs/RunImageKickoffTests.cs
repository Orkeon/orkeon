using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Validation;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Replay;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>The images of a run's input, through the host's own composition: shown to every task, or told as not shown.</summary>
public sealed class RunImageKickoffTests
{
    private const string Png = "/inbox/photo.png";

    private static readonly byte[] s_png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] s_jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class VendorModule : ILlmProviderModule
    {
        public string Key => "fake-cli";

        public List<(string? Model, LlmMessage[] Messages)> Calls { get; } = [];

        public HashSet<string> FailingModels { get; } = [];

        public ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context)
        {
            var vendor = new MockLlmProvider
            {
                Name = Key, BaseConfig = config, Capabilities = LlmProviderCapabilities.Unknown with { Vision = true },
            };
            vendor.SetChatFunc((messages, call) =>
            {
                var model = string.IsNullOrWhiteSpace(call?.Model) ? config.Model : call.Model;
                lock (Calls)
                    Calls.Add((model, messages));
                return FailingModels.Contains(config.Model!)
                    ? throw new HttpRequestException("unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable)
                    : new LlmResponse { Content = "a plain answer", TokensUsed = 2, PromptTokens = 1, CompletionTokens = 1 };
            });
            return vendor;
        }
    }

    private sealed class Setup
    {
        public VendorModule Vendor { get; } = new();

        public FakeFileSystemService Files { get; } = new FakeFileSystemService()
            .AddMount("/inbox").AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create)
            .AddFile(Png, s_png);

        public MultiModalOptions Options { get; } = new();

        public Dictionary<string, string?> Settings { get; } = new() { ["Llm:Provider"] = "fake-cli", ["Llm:Model"] = "gpt-4o" };

        public string Process { get; set; } = "sequential";

        public string FileSystem { get; set; } = string.Empty;

        public async Task<CrewOutput> RunAsync(params string[] images)
        {
            var yaml = $$"""
                name: pictures
                goal: Read the pictures
                process: {{Process}}
                agents:
                  looker:
                    role: Looker
                    goal: Look
                    {{FileSystem}}
                  writer:
                    role: Writer
                    goal: Write
                tasks:
                  look:
                    description: Look at the picture
                    expected_output: A description
                    agent: looker
                  write:
                    description: Write about the picture
                    expected_output: A text
                    agent: writer
                """;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSingleton<IFileSystemService>(Files);
            services.AddSingleton<ILlmProviderModule>(Vendor);
            services.AddSingleton<IOptions<MultiModalOptions>>(new OptionsWrapper<MultiModalOptions>(Options));
            services.AddOrkeonLlmProvider(
                sp => ((LlmProviderAdapter)sp.GetRequiredService<ILlmProviderFactory>().Create(
                    Orkeon.Infrastructure.LLMs.Profiles.LlmSettings.ReadDefault(configuration))).UnderlyingProvider,
                LlmConfig.Create(Settings["Llm:Model"]!));
            services.AddOrkeonApplication();
            services.AddOrkeonInfrastructure();
            await using var container = services.BuildServiceProvider();
            await using var scope = container.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
            var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
            return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
                crew.Id,
                new CrewInput("go", new Dictionary<string, object>()) { Images = [.. images.Select(i => new CrewInputImage { Path = i })] },
                Ct);
        }
    }

    private static List<ImageContentPart> ImagesOf(LlmMessage[] messages) =>
        [.. messages.Where(m => m.MultiModalContent is not null).SelectMany(m => m.MultiModalContent!.Parts).OfType<ImageContentPart>()];

    private static string TextOf(LlmMessage[] messages) => string.Join('\n', messages.Select(m => m.Content));

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    public async Task ShouldShowTheImageInTheFirstUserTurnOfEveryTask(string process)
    {
        var run = new Setup { Process = process };

        var output = await run.RunAsync(Png);

        Assert.True(output.Succeeded, output.Error);
        Assert.Empty(output.UnseenImages);
        foreach (var description in (string[])["Look at the picture", "Write about the picture"])
        {
            var call = Assert.Single(run.Vendor.Calls, c => TextOf(c.Messages).Contains("Task: " + description, StringComparison.Ordinal)
                || c.Messages.First(m => m.Role == "user").Content.Contains(description, StringComparison.Ordinal));
            var image = Assert.Single(ImagesOf(call.Messages));
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(s_png, image.Data!);
            Assert.NotNull(call.Messages.First(m => m.Role == "user").MultiModalContent);
        }
    }

    [Fact]
    public async Task ShouldNameThePathAndTellThePerson_WhenTheModelIsNotKnownToReadImages()
    {
        var run = new Setup();
        run.Settings["Llm:Model"] = "some-text-model";

        var output = await run.RunAsync(Png);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["Looker", "Writer"], output.UnseenImages.Select(u => u.Reader).Order());
        Assert.All(output.UnseenImages, u => Assert.Contains("some-text-model", u.Reason, StringComparison.Ordinal));
        Assert.All(run.Vendor.Calls, c => Assert.Empty(ImagesOf(c.Messages)));
        Assert.All(run.Vendor.Calls, c => Assert.Contains(Png, TextOf(c.Messages), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldShowNothingToAnAgent_WhenItsFilesystemExcludesTheInbox()
    {
        var run = new Setup { FileSystem = "filesystem: [/output]" };

        var output = await run.RunAsync(Png);

        Assert.True(output.Succeeded, output.Error);
        var unseen = Assert.Single(output.UnseenImages);
        Assert.Equal("Looker", unseen.Reader);
        Assert.Contains("filesystem", unseen.Reason, StringComparison.Ordinal);
        Assert.Single(run.Vendor.Calls, c => ImagesOf(c.Messages).Count == 1);
    }

    [Fact]
    public async Task ShouldJudgeTheBytes_NotTheName()
    {
        var run = new Setup();
        run.Files.AddFile("/inbox/a.png", s_jpeg).AddFile("/inbox/b.png", "<svg/>"u8.ToArray()).AddFile("/inbox/c.gif", "GIF89a.."u8.ToArray())
            .AddFile("/inbox/d.webp", "RIFF1234WEBPxx"u8.ToArray());

        var output = await run.RunAsync("/inbox/a.png", "/inbox/b.png", "/inbox/c.gif", "/inbox/d.webp", "/inbox/gone.png");

        Assert.Equal(["image/jpeg", "image/gif", "image/webp"], ImagesOf(run.Vendor.Calls[0].Messages).Select(i => i.MimeType));
        Assert.Equal(["/inbox/b.png", "/inbox/gone.png"], output.UnseenImages.Where(u => u.Reader == "Looker").Select(u => u.Path));
    }

    [Fact]
    public async Task ShouldBoundTheCountAndTheSize()
    {
        var run = new Setup();
        run.Options.MaxImages = 1;
        run.Options.MaxImageSizeBytes = 100;
        run.Files.AddFile("/inbox/big.png", [.. s_png, .. new byte[200]]).AddFile("/inbox/second.png", s_png);

        var output = await run.RunAsync("/inbox/big.png", Png, "/inbox/second.png");

        Assert.All(run.Vendor.Calls, c => Assert.Single(ImagesOf(c.Messages)));
        Assert.Contains(output.UnseenImages, u => u.Path == "/inbox/big.png" && u.Reason.Contains("100 bytes", StringComparison.Ordinal));
        Assert.Contains(output.UnseenImages, u => u.Path == "/inbox/second.png" && u.Reason.Contains("at most 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldShowNone_WhenTurnedOff()
    {
        var run = new Setup();
        run.Options.Enabled = false;

        var output = await run.RunAsync(Png);

        Assert.Equal(2, output.UnseenImages.Count);
        Assert.All(run.Vendor.Calls, c => Assert.Empty(ImagesOf(c.Messages)));
    }

    [Fact]
    public async Task ShouldLeaveTheCallsAsTheyWere_WhenTheRunHasNoImage()
    {
        var run = new Setup();

        var output = await run.RunAsync();

        Assert.Empty(output.UnseenImages);
        Assert.All(run.Vendor.Calls, c => Assert.All(c.Messages, m => Assert.Null(m.MultiModalContent)));
    }

    private static LlmConfig Config(string model) => Orkeon.Infrastructure.LLMs.Profiles.LlmSettings.ReadDefault(
        new ConfigurationBuilder().AddInMemoryCollection([new("Llm:Provider", "fake-cli"), new("Llm:Model", model)]).Build());

    private static LlmProviderFactory Factory(Setup run, LlmReplayMode mode) =>
        new(new NoHttpClientFactory(), NullLoggerFactory.Instance, run.Files, new LlmProviderFactoryOptions
        {
            Modules = [run.Vendor],
            Images = new RunImageSource(run.Files, run.Options),
            Replay = new LlmReplayOptions { Mode = mode, Cassette = CassetteFixture },
        });

    private const string CassetteFixture = "/llm-cassette/run.jsonl";

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static async Task<LlmResponse> AskAsync(ILlmProvider provider)
    {
        using var images = RunImages.Begin([new CrewInputImage { Path = Png }]);
        using var turn = RunImages.OpenTurn("Looker");
        return await provider.ChatAsync([new LlmMessage { Role = "user", Content = "hello" }], null, Ct);
    }

    [Fact]
    public async Task ShouldReplayACallOnlyWithTheSameBytes()
    {
        var run = new Setup();
        run.Files.AddMount("/llm-cassette", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create);
        var config = Config("gpt-4o");
        static ILlmProvider Of(IBasicLlmProvider built) => ((LlmProviderAdapter)built).UnderlyingProvider;
        await AskAsync(Of(Factory(run, LlmReplayMode.Record).Create(config)));
        Assert.Single(ImagesOf(Assert.Single(run.Vendor.Calls).Messages));
        var cassette = await run.Files.TryReadAllTextAsync(CassetteFixture, Ct);
        Assert.DoesNotContain(Convert.ToBase64String(s_png), cassette, StringComparison.Ordinal);

        var replayed = await AskAsync(Of(Factory(run, LlmReplayMode.Replay).Create(config)));
        Assert.Equal("a plain answer", replayed.Content);
        Assert.Single(run.Vendor.Calls);

        run.Files.AddFile(Png, [.. s_png, 77]);
        await Assert.ThrowsAsync<ReplayMissException>(() => AskAsync(Of(Factory(run, LlmReplayMode.Replay).Create(config))));
    }

    [Theory]
    [InlineData("some-text-model", "gpt-4o", true)]
    [InlineData("gpt-4o", "some-text-model", false)]
    public async Task ShouldDecideOnTheModelThatAnswers_WhenAProfileFallsBack(string first, string second, bool seen)
    {
        var run = new Setup();
        run.Vendor.FailingModels.Add(first);
        var factory = Factory(run, LlmReplayMode.Off);
        ILlmProvider Link(string model) =>
            ((LlmProviderAdapter)factory.Create(Config(model))).UnderlyingProvider;
        var chain = new Orkeon.Infrastructure.LLMs.Profiles.FallbackLlmProvider(
            "a", [new("a", Link(first)), new("b", Link(second))], null, NullLogger.Instance, null, TimeProvider.System);

        using var images = RunImages.Begin([new CrewInputImage { Path = Png }]);
        using (var turn = RunImages.OpenTurn("Looker"))
        {
            await chain.ChatAsync([new LlmMessage { Role = "user", Content = "hello" }], null, Ct);
            turn!.Answered();
        }

        Assert.Equal(seen ? 1 : 0, ImagesOf(run.Vendor.Calls[^1].Messages).Count);
        Assert.Equal(seen ? 0 : 1, RunImages.Unseen.Count);
    }
}
