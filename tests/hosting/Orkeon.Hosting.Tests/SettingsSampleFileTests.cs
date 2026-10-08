using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The sample settings file of an installation, <c>appsettings.sample.json</c>, was a copy of the
/// examples' default file: twelve keys, at the values of a local model. It is produced from the
/// settings catalogue now — every key a shipped binary reads, by category —, and it can be copied:
/// put in place of the settings file as it is, no host refuses it, none reports it, and each host
/// binds what it binds without a file.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed partial class SettingsSampleFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-settings-sample-" + Guid.NewGuid().ToString("N"));
    private readonly string _sample;

    public SettingsSampleFileTests()
    {
        Directory.CreateDirectory(_root);
        _sample = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(_sample, Produced);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static string Produced { get; } = SettingsSampleFile.Produce(SettingsCatalog.Complete);

    private static SettingsCatalog Shipped { get; } = SettingsCatalog.Complete.ForShippedHosts();

    [Fact]
    public void The_committed_file_is_what_the_catalogue_produces() =>
        ProducedFile.AssertCurrent(SettingsSampleFile.RepositoryPath, Produced, SettingsSampleFile.Regenerate);

    [Fact]
    public void A_run_host_takes_it_without_a_refusal_or_a_notice()
    {
        var verdict = RunnerHost.InspectSettings(_sample, (_, services) =>
        {
            services.AddOrkeonHumanInput();
            services.AddSemanticSearchTool();
        });

        Assert.Empty(verdict.Refusals);
        Assert.Empty(verdict.Notices);
        Assert.Null(RunnerExecution.CheckSettingsMounts(_sample));
        Assert.Empty(RunnerSettings.ReadDeclaredMounts(_sample));
    }

    /// <summary>
    /// What a copy must not do: one key of <c>Llm</c> written, even at its default, and the host has
    /// a default model — the warning that none is configured goes, and the run answers nothing.
    /// </summary>
    [Fact]
    public void Copied_as_it_is_it_leaves_the_host_without_a_default_model_and_the_host_says_so()
    {
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(_sample, new RunnerMountPlan());

            Assert.False(LlmSettings.HasDefault(host.Services.GetRequiredService<IConfiguration>()));
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Contains(RunnerHost.LlmNotConfiguredMessage, stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every options section the run host binds holds, with the file, the values it holds without
    /// it: lists not lengthened, dictionaries not filled, no value moved.
    /// </summary>
    [Fact]
    public void Loading_it_changes_nothing_a_host_binds()
    {
        var without = Bound(settings: null);
        var with = Bound(_sample);

        Assert.NotEmpty(without);
        Assert.Equal(without.Keys.Order(StringComparer.Ordinal), with.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(without
            .Where(pair => !string.Equals(pair.Value, with[pair.Key], StringComparison.Ordinal))
            .Select(pair => $"{pair.Key}: {pair.Value} without the file, {with[pair.Key]} with it"));
    }

    /// <summary>Each key the file writes is a key of the catalogue, at the catalogue's default.</summary>
    [Fact]
    public void Each_key_it_writes_holds_the_default_of_the_catalogue()
    {
        var written = Written();

        Assert.NotEmpty(written);
        foreach (var (path, value) in written)
        {
            if (Shipped.Setting(path) is not { } entry)
            {
                Assert.Fail($"{path} is written and is no key of the catalogue");
                return;
            }

            Assert.True(SettingsSampleFile.Writes(entry), $"{path} is written and should be shown");
            using var literal = JsonDocument.Parse(entry.Default!);
            Assert.Equal(AsConfigurationValue(literal.RootElement), value);
        }

        Assert.Equal(Shipped.Settings.Count(SettingsSampleFile.Writes), written.Count);
    }

    /// <summary>The cited case: the model-call limits, at the engine's defaults — not a local model's.</summary>
    [Theory]
    [InlineData("RateLimiting:GlobalRequestsPerMinute", "60")]
    [InlineData("RateLimiting:ProviderRequestsPerMinute", "30")]
    [InlineData("RateLimiting:AgentRequestsPerMinute", "20")]
    [InlineData("RateLimiting:MaxConcurrentRequests", "0")]
    [InlineData("RateLimiting:QueueLimit", "5")]
    public void The_model_call_limits_are_written_at_the_engines_defaults(string key, string value) =>
        Assert.Equal(value, Written()[key]);

    /// <summary>Every key a shipped binary reads is in the file once, written or shown, and no key of a section only a C# host reads.</summary>
    [Fact]
    public void Every_key_a_shipped_binary_reads_is_there_once_and_no_other()
    {
        var named = NamedPaths(Produced);

        var twice = named.GroupBy(path => path, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key);
        Assert.Empty(twice);
        Assert.Equal(
            Shipped.Settings.Select(entry => entry.Path).Order(StringComparer.Ordinal),
            named.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(named, path => SettingsCatalog.Complete.SectionCovering(path) is { Hosts.Count: 0 });
    }

    [Fact]
    public void No_secret_is_written_nor_a_path_or_a_date_of_the_machine_that_produced_it()
    {
        var written = Written();

        Assert.DoesNotContain(Shipped.Settings.Where(entry => entry.Secret), entry => written.ContainsKey(entry.Path));
        Assert.DoesNotContain("\"ApiKey\": \"", Produced, StringComparison.Ordinal);
        string[] ofTheMachine =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            ProducedFile.RepositoryRoot(),
            Environment.MachineName,
        ];
        Assert.All(ofTheMachine.Where(text => text.Length > 1), text => Assert.DoesNotContain(text, Produced, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            Shipped.Settings.Where(entry => written.ContainsKey(entry.Path)),
            entry => entry.Type == "date-time" || entry.DefaultNote is not null);
    }

    /// <summary>The file reads in 100 columns, a list's entries apart, and holds no tab.</summary>
    [Fact]
    public void Its_comments_fit_the_terminal_they_are_read_in()
    {
        var lines = Produced.Split('\n');

        Assert.DoesNotContain(lines, line => line.Contains('\t', StringComparison.Ordinal));
        Assert.All(
            lines.Where(line => line.TrimStart().StartsWith("// ", StringComparison.Ordinal) && !MemberLine().IsMatch(line)),
            line => Assert.True(line.Length <= 100, line));
    }

    /// <summary>The two packagers copy this file, under the name every channel installs.</summary>
    [Theory]
    [InlineData("scripts/package-installers.sh")]
    [InlineData("scripts/package-installers.ps1")]
    public void The_packagers_ship_the_produced_file(string script)
    {
        var text = ProducedFile.Read(script);

        Assert.NotNull(text);
        var copy = Assert.Single(text.Split('\n'), line => line.Contains("appsettings.sample.json", StringComparison.Ordinal)
                                                         && !line.TrimStart().StartsWith('#'));
        Assert.DoesNotContain("examples", copy, StringComparison.Ordinal);
        Assert.Matches(@"ASSETS|\$Assets", copy);
        Assert.EndsWith("installer-assets/appsettings.sample.json", SettingsSampleFile.RepositoryPath, StringComparison.Ordinal);
    }

    /// <summary>The values the run host binds from <paramref name="settings"/>, by options section and key.</summary>
    private static Dictionary<string, string> Bound(string? settings)
    {
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            using var host = RunnerHost.Build(settings, new RunnerMountPlan());

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var declaration in host.Services.GetServices<SettingsDeclaration>().Where(declaration => declaration.Evaluate is not null))
            {
                if (declaration.Shape.IsAbstract
                    || host.Services.GetService(typeof(IOptions<>).MakeGenericType(declaration.Shape)) is not { } options
                    || options.GetType().GetProperty(nameof(IOptions<object>.Value))?.GetValue(options) is not { } bound)
                {
                    continue;
                }

                foreach (var entry in SettingsCatalogBuilder.ValuesOf(declaration.Path, bound))
                    values[$"{declaration.Shape.Name} {entry.Path}"] = $"{entry.Default} | {entry.DefaultNote}";
            }

            return values;
        }
        finally
        {
            Console.SetError(original);
        }
    }

    /// <summary>What a configuration reads from the file alone: the keys it writes, comments set aside.</summary>
    private Dictionary<string, string?> Written() =>
        new ConfigurationBuilder().AddJsonFile(_sample, optional: false).Build().AsEnumerable()
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>A JSON literal as the configuration holds it: a text as it is, a number or a switch as written (<c>True</c>, <c>False</c>).</summary>
    private static string AsConfigurationValue(JsonElement literal) => literal.ValueKind switch
    {
        JsonValueKind.String => literal.GetString()!,
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => literal.GetRawText(),
    };

    /// <summary>
    /// The path of every member the file names, written or shown, read off its layout: one member a
    /// line, two spaces a level, <c>// </c> before a shown one, <c>{</c> alone for the item of a list.
    /// </summary>
    private static List<string> NamedPaths(string file)
    {
        var paths = new List<string>();
        var open = new List<string>();
        foreach (var raw in file.Split('\n'))
        {
            var indent = raw.Length - raw.TrimStart(' ').Length;
            if (indent == 0)
                continue;

            var line = raw.TrimStart(' ');
            if (line.StartsWith("// ", StringComparison.Ordinal))
                line = line[3..];
            var depth = indent / 2;
            if (open.Count > depth - 1)
                open.RemoveRange(depth - 1, open.Count - (depth - 1));

            if (line is "{")
            {
                open.Add(SettingsCatalogBuilder.Index);
                continue;
            }

            if (Member().Match(line) is not { Success: true } member)
                continue;

            var name = member.Groups["name"].Value;
            var rest = member.Groups["rest"].Value;
            if (rest is "{" or "[")
                open.Add(name);
            else
                paths.Add(string.Join(':', open.Append(name)));
        }

        return paths;
    }

    [GeneratedRegex("""^"(?<name>[^"]+)": (?<rest>.+)$""")]
    private static partial Regex Member();

    [GeneratedRegex("""^\s*// "[^"]+": """)]
    private static partial Regex MemberLine();
}
