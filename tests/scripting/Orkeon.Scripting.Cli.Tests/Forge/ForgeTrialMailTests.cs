using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Email.DependencyInjection;
using Orkeon.Tools.Email.Trial;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using CrewAggregate = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>FORGE-10 — every try of the forge bench runs inside a mail trial, seeded with the sample and the mails of the need.</summary>
public sealed class ForgeTrialMailTests : IDisposable
{
    private const string TwoMails =
        """{"messages":[{"folder":"Inbox","from":"Ops <ops@example.com>","to":["you@example.com"],"subject":"Journal of 11 October","body":"Two incidents overnight."},{"folder":"Inbox","from":"boss@example.com","to":["you@example.com"],"subject":"Send me the summary","body":"Every morning please."}]}""";

    private static readonly string[] MailTool = ["email_send"];
    private static readonly string[] Recipients = ["client@example.com"];
    private static readonly string[] FileTool = ["file_read"];

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-trial-mail-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-03")]
    public async Task ShouldRunTheTryInsideAMailTrial_AndRecordItsActions()
    {
        var fake = new FakeMailboxProvider();
        var probe = new Probe();
        using var provider = BuildHost(fake, probe);
        var bench = new ForgeCrewTestBench(provider, workspace: _workspace);

        var run = await bench.ExecuteAsync(ForgeSession.Create(_workspace, "mail-trial"), 1, TestContext.Current.CancellationToken);

        Assert.True(run.Success, run.Error);
        var action = Assert.Single(run.MailActions);
        Assert.Equal("send", action.Kind);
        Assert.Equal(["client@example.com"], action.Recipients);
        Assert.Equal("Morning summary", action.Subject);
        Assert.Contains("All quiet.", action.Body);
        Assert.Empty(fake.Requests);
        Assert.Null(EmailTrial.Current);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public async Task ShouldEnterTheMailTrial_WhenTheBenchHasNoWorkspace()
    {
        var probe = new Probe();
        using var provider = BuildHost(new FakeMailboxProvider(), probe);
        var session = ForgeSession.Create(_workspace, "mail-trial-bare");
        var bench = new ForgeCrewTestBench(provider);

        var run = await bench.ExecuteAsync(session, 1, TestContext.Current.CancellationToken);

        Assert.True(run.Success, run.Error);
        Assert.True(probe.TrialSeen);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async Task ShouldSeedTheTrialWithTheSampleAndTheNeedDrawnMails()
    {
        var stub = new StubTrialMailLlmProvider { Content = TwoMails };
        var session = ForgeSession.Create(_workspace, "mail-seed");

        var mailbox = await new ForgeTrialMailAuthor(stub)
            .SeedAsync(session, new ForgeBrief { Goal = "Mail me a daily summary" }, MailTool, TestContext.Current.CancellationToken);

        Assert.Equal(14, mailbox.Seed.Messages.Count);
        Assert.Contains(mailbox.Seed.Messages, m => m.Subject == "Journal of 11 October");
        Assert.Contains(mailbox.Seed.Messages, m => m.Subject == "Send me the summary");
        Assert.Equal("", mailbox.Note);
        Assert.True(File.Exists(Path.Combine(session.Directory, ForgeTrialMailAuthor.FileName)));
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async Task ShouldDrawTheSeedOncePerSession_AndOnlyWhenTheCrewNamesAMailTool()
    {
        var stub = new StubTrialMailLlmProvider { Content = TwoMails };
        var session = ForgeSession.Create(_workspace, "mail-once");
        var author = new ForgeTrialMailAuthor(stub);
        var brief = new ForgeBrief { Goal = "Mail me a daily summary" };
        var ct = TestContext.Current.CancellationToken;

        var without = await author.SeedAsync(session, brief, FileTool, ct);

        Assert.Equal(0, stub.Calls);
        Assert.Equal(12, without.Seed.Messages.Count);
        Assert.False(File.Exists(Path.Combine(session.Directory, ForgeTrialMailAuthor.FileName)));

        var first = await author.SeedAsync(session, brief, MailTool, ct);
        var second = await author.SeedAsync(session, brief, MailTool, ct);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(14, first.Seed.Messages.Count);
        Assert.Equal(14, second.Seed.Messages.Count);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async Task ShouldRunOnTheSampleAlone_AndSaySo_WhenDrawingFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var failing = new StubTrialMailLlmProvider { Failure = new InvalidOperationException("model down") };
        var session = ForgeSession.Create(_workspace, "mail-fail");

        var mailbox = await new ForgeTrialMailAuthor(failing)
            .SeedAsync(session, new ForgeBrief { Goal = "Mail me a daily summary" }, MailTool, ct);

        Assert.Equal(12, mailbox.Seed.Messages.Count);
        Assert.NotEmpty(mailbox.Note);
        Assert.Contains("sample", mailbox.Note, StringComparison.OrdinalIgnoreCase);

        var again = await new ForgeTrialMailAuthor(failing).SeedAsync(session, new ForgeBrief { Goal = "Mail me a daily summary" }, MailTool, ct);
        Assert.Equal(1, failing.Calls);
        Assert.Equal(mailbox.Note, again.Note);

        using var provider = BuildHost(new FakeMailboxProvider(), new Probe());
        var bench = new ForgeCrewTestBench(provider, workspace: _workspace, mailAuthor: new ForgeTrialMailAuthor(failing));

        var run = await bench.ExecuteAsync(ForgeSession.Create(_workspace, "mail-fail-run"), 1, ct);

        Assert.True(run.Success, run.Error);
        Assert.NotEmpty(run.MailSeedNote);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public void ShouldKnowEveryMailboxToolTheEmailFamilyRegisters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonEmailTools(new ConfigurationBuilder().Build());
        using var host = services.BuildServiceProvider();

        var names = host.GetServices<IBaseTool>().Select(t => t.Name).Where(n => n != "email_parser").ToHashSet(StringComparer.Ordinal);

        Assert.True(names.SetEquals(ForgeTrialMailAuthor.MailTools), string.Join(",", names.Order(StringComparer.Ordinal)));
    }

    private static ServiceProvider BuildHost(FakeMailboxProvider fake, Probe probe)
    {
        if (!ComponentBase.IsDefaultSerializerConfigured)
            ComponentBase.DefaultSerializer = JsonComponentSerializer.Instance;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<Orkeon.Tools.Email.Mailboxes.IMailboxProvider>(new Orkeon.Tools.Email.Trial.ScopedMailboxProvider(fake));
        services.AddOrkeonEmailTools(new ConfigurationBuilder().Build());
        services.AddSingleton(probe);
        services.AddSingleton<ICrewDefinitionLoader>(new StubCrewDefinitionLoader());
        services.AddScoped<ICrewFactory, FakeCrewFactory>();
        services.AddScoped<ICrewOrchestrationService, FakeMailingOrchestrator>();
        return services.BuildServiceProvider();
    }

    private sealed class Probe
    {
        public bool TrialSeen { get; set; }
    }

    private sealed class StubCrewDefinitionLoader : ICrewDefinitionLoader
    {
        private static readonly CrewConfiguration Configuration = new()
        {
            Name = "mailer",
            Goal = "Send a summary",
            Agents = [new AgentConfiguration { Role = "Mailer", Goal = "Send", Tools = ["email_send"] }],
        };

        public Task<CrewConfiguration> LoadFromDirectoryAsync(string directoryPath, CancellationToken ct = default) =>
            Task.FromResult(Configuration);

        public Task<CrewConfiguration> LoadFromFileAsync(string filePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewConfiguration> LoadFromStringAsync(string yamlContent, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public CrewDefinitionValidationResult Validate(CrewConfiguration config) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCrewFactory : ICrewFactory
    {
        public Task<CrewAggregate> CreateFromConfigAsync(CrewConfiguration config, CancellationToken ct = default) =>
            Task.FromResult(new Orkeon.Domain.Crew.CrewBuilder()
                .Goal(config.Goal)
                .Sequential()
                .WithAgent(a => a.Role("Worker").Goal("Work"))
                .WithTask(t => t.Description("Do the work.").ExpectedOutput("The work."))
                .Build());

        public Task<CrewAggregate> CreateFromFileAsync(string yamlFilePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CrewAggregate> CreateFromDirectoryAsync(string directoryPath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Calls <c>email_send</c> as the crew's model would.</summary>
    private sealed class FakeMailingOrchestrator(IServiceProvider services, Probe probe) : ICrewOrchestrationService
    {
        public async Task<CrewOutput> KickoffAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default)
        {
            probe.TrialSeen = EmailTrial.Current is not null;
            var tool = services.GetServices<IBaseTool>().Single(t => t.Name == "email_send");
            var response = await tool.CallAsync(
                new ToolCallRequest("email_send", new Dictionary<string, object?>
                {
                    ["to"] = Recipients,
                    ["subject"] = "Morning summary",
                    ["text"] = "All quiet.",
                }),
                cancellationToken);
            var task = new TaskOutput("work", AgentId: null, response.Success ? "sent" : "failed", DateTime.UtcNow, Success: true, TimeSpan.Zero);
            return new CrewOutput("done", [task], TimeSpan.Zero, TokensUsed: null);
        }

        public Task<BatchOutput> KickoffForEachAsync(CrewId crewId, IEnumerable<CrewInput> inputs, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrewExecutionId> KickoffAsyncNoWait(CrewId crewId, CrewInput input) =>
            throw new NotSupportedException();

        public Task<CrewExecutionStatus> GetExecutionStatusAsync(CrewExecutionId executionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<CrewExecutionEvent> KickoffStreamingAsync(CrewId crewId, CrewInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
