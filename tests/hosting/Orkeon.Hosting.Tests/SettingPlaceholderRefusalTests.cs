using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Configuration;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>The refusal of a variable reference names the setting and its correction, never the value.</summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class SettingPlaceholderRefusalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-placeholder-" + Guid.NewGuid().ToString("N"));

    public SettingPlaceholderRefusalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    [Trait("BR", "CH-14")]
    public void ShouldNameTheKeyAndNothingOfTheValue_WhenThePhraseIsWritten()
    {
        const string path = "Orkeon:Redis:ConnectionString";

        var correction = SettingPlaceholders.CorrectionFor(path);

        Assert.NotNull(correction);
        var phrase = OperatorMessages.SettingPlaceholderRefused(path, correction);
        Assert.Contains(path, phrase, StringComparison.Ordinal);
        Assert.Contains(correction, phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("host", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(SettingPlaceholders.CorrectionFor("mcp:servers:gh:args:2"));
        Assert.Null(SettingPlaceholders.CorrectionFor("Orkeon:Rag:SystemPrompt"));
    }

    /// <summary>
    /// One row per entry of the list a <c>RunnerHost</c> reads. The daemon-only entries
    /// (<c>A2A:Security:*</c>, <c>Orkeon:Host:*</c>) belong to step 4 and are left out here.
    /// </summary>
    public static TheoryData<string, string> Rows()
    {
        var data = new TheoryData<string, string>();
        foreach (var setting in SettingPlaceholders.Settings)
        {
            if (setting.Path.StartsWith("A2A:Security:", StringComparison.Ordinal)
                || setting.Path.StartsWith("Orkeon:Host:", StringComparison.Ordinal))
                continue;

            var path = Concrete(setting.Path);
            data.Add(path, path == "MCP:Servers:s:Url" ? "http://${H}/mcp" : "${X}");
        }

        return data;
    }

    private static string Concrete(string pattern)
    {
        var segments = pattern.Split(':');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] != "*")
                continue;
            segments[i] = i < segments.Length - 1
                ? (segments[i - 1] == "Servers" ? "s" : "p")
                : segments[i - 1] switch
                {
                    "Args" => "0",
                    "Env" => "GITHUB_TOKEN",
                    "Secrets" => "PEER_KEY",
                    _ => "ITEM",
                };
        }

        return string.Join(':', segments);
    }

    private static JsonObject Nest(string path, string value)
    {
        var root = new JsonObject { ["RaggableTree"] = new JsonObject { ["Enabled"] = false } };
        var node = root;
        var segments = path.Split(':');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (node[segments[i]] is not JsonObject next)
                node[segments[i]] = next = new JsonObject();
            node = next;
        }

        node[segments[^1]] = value;
        return root;
    }

    private (IHost? Host, string Output, IReadOnlyList<CapturedLogEntry> Logs, Exception? Error) TryBuild(
        string json, Action<IHostBuilder>? configureBuilder = null)
    {
        var settings = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(settings, json);
        using var logs = new CapturingLoggerProvider();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        Console.SetOut(output);
        Console.SetError(output);
        try
        {
            var host = RunnerHost.Build(
                settings,
                new RunnerMountPlan(),
                configureLogging: (_, b) =>
                {
                    b.AddProvider(logs);
                    b.SetMinimumLevel(LogLevel.Information);
                },
                configureBuilder: configureBuilder);
            return (host, output.ToString(), logs.Entries, null);
        }
        catch (RunnerSettingsException ex)
        {
            return (null, output.ToString(), logs.Entries, ex);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Theory]
    [Trait("BR", "CH-14")]
    [MemberData(nameof(Rows))]
    public void ShouldRefuseTheHostWithTheCorrection_ForEachSettingOfTheList(string path, string value)
    {
        var (host, output, logs, error) = TryBuild(Nest(path, value).ToJsonString());
        host?.Dispose();

        var refused = Assert.IsType<RunnerSettingsException>(error);
        var correction = SettingPlaceholders.CorrectionFor(path);
        Assert.NotNull(correction);
        Assert.Contains(path, refused.Message, StringComparison.Ordinal);
        Assert.Contains(correction, refused.Message, StringComparison.Ordinal);
        Assert.Contains(OperatorMessages.SettingPlaceholderRefused(path, correction), refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, refused.Message, StringComparison.Ordinal);

        if (path == "Llm:BaseUrl")
        {
            var refusedPhrase = OperatorMessages.LlmBaseUrlRefused(path, "x");
            var tail = refusedPhrase[(refusedPhrase.IndexOf("', ", StringComparison.Ordinal) + 3)..];
            var written = output + string.Join('\n', logs.Select(l => l.Message)) + refused.Message;
            Assert.DoesNotContain("WARNING:", written, StringComparison.Ordinal);
            Assert.DoesNotContain("LLM resolved:", written, StringComparison.Ordinal);
            Assert.DoesNotContain(tail, written, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("BR", "CH-15")]
    public void ShouldAccept_WhenTheValueIsNotAReferenceOrIsCovered()
    {
        var notReferences = """
            {
              "RaggableTree": { "Enabled": false },
              "Llm": { "BaseUrl": "https://h/%C3%A9t%C3%A9", "Model": "m", "ApiKeyEnvVar": "SOME_KEY" },
              "Orkeon": { "Rag": { "Generation": { "SystemPrompt": "Answer ${x} briefly." } } },
              "MCP": { "Servers": { "s": {
                "Command": "demo-mcp", "Args": ["%d"],
                "Env": { "PRICE": "$5", "DB_PASSWORD": "$ecret2024" } } } }
            }
            """;
        var (host, _, _, error) = TryBuild(notReferences);
        host?.Dispose();
        Assert.Null(error);

        // A reference in the file, covered by a higher layer: the value the host reads is not one.
        var covered = """
            {
              "RaggableTree": { "Enabled": false },
              "MCP": { "Servers": { "s": { "Command": "demo-mcp", "Env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" } } } }
            }
            """;
        var (coveredHost, _, _, coveredError) = TryBuild(covered, builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP:Servers:s:Env:GITHUB_TOKEN"] = "real-value",
                })));
        coveredHost?.Dispose();
        Assert.Null(coveredError);
    }

    /// <summary>Why a sensitive-looking property needs no entry of the list.</summary>
    private static readonly Dictionary<string, string> s_excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Orkeon:TokenCounter:CharsPerToken"] = "A number of characters per token: it holds no credential, address or command.",
    };

    private sealed class WitnessOptions
    {
        public string? FooApiKey { get; set; }
    }

    private static readonly string[] s_sensitiveSuffixes =
    [
        "ApiKey", "Secret", "SecretName", "SecretNames", "Password", "Token", "ConnectionString", "Url", "Uri",
        "Endpoint", "Host", "Authority", "Command", "Args", "Env", "EnvVar", "EnvironmentVariable",
    ];

    private static void Walk(string path, string name, SettingsShape shape, ISet<string> found)
    {
        if (shape.Named.Count == 0 && s_sensitiveSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            found.Add(path);

        foreach (var (key, child) in shape.Named)
            Walk(path + ":" + key, key, child, found);
        if (shape.Entries is not null)
            Walk(path + ":*", name, shape.Entries, found);
    }

    private static List<string> Uncovered(IEnumerable<(string Path, Type Shape)> declarations)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, type) in declarations)
        {
            var segments = path.Split(':');
            Walk(path, segments[^1], SettingsShape.Of(type), found);
        }

        return found
            .Where(p => SettingPlaceholders.CorrectionFor(p) is null && SettingPlaceholders.CorrectionFor(p + ":*") is null)
            .Where(p => !s_excluded.ContainsKey(p))
            .ToList();
    }

    [Fact]
    [Trait("BR", "CH-14")]
    public void ShouldListEverySensitiveProperty_WhenTheDeclaredShapesAreWalked()
    {
        // The daemon's shapes (Orkeon.Host) are not reachable from Orkeon.Hosting.Tests: no reference.
        var witness = Uncovered([("Witness", typeof(WitnessOptions))]);
        Assert.Equal(["Witness:FooApiKey"], witness);

        var (host, _, _, error) = TryBuild("""{ "RaggableTree": { "Enabled": false } }""");
        Assert.Null(error);
        using (host)
        {
            var declarations = host!.Services.GetServices<SettingsDeclaration>().Select(d => (d.Path, d.Shape)).ToList();
            Assert.NotEmpty(declarations);

            var uncovered = Uncovered(declarations);

            Assert.True(uncovered.Count == 0, "Sensitive properties neither listed nor excluded: " + string.Join(", ", uncovered));
        }
    }
}
