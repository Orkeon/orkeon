using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-35 — every setting <see cref="RunnerHost.Build"/> refuses comes out as one type,
/// <see cref="RunnerSettingsException"/>, whose message names the key — or the file and the
/// place in it. The entry points translate that type (exit 1 for <c>orkeon</c>, 78 for
/// <c>orkeon-host</c>); before, a retired key, an unconvertible value, an unreadable file or a
/// malformed address each left the build as its own exception, and most of them crashed the
/// program. The refusals the host already raised — invalid <c>Llm:Profiles</c>, an unknown
/// <c>Orkeon:Rag:LlmProfile</c>, <c>MCP:EnableServer</c>, mounts — are held by their own classes.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostSettingsRefusalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-settings-refusal-" + Guid.NewGuid().ToString("N"));

    public RunnerHostSettingsRefusalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private RunnerSettingsException Refusal(string json)
    {
        var settings = Settings(json);
        return Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, new RunnerMountPlan()));
    }

    [Fact]
    public void A_key_the_RaggableTree_section_no_longer_carries_is_refused_by_its_name()
    {
        var error = Refusal("""{ "RaggableTree": { "Exclude": ["bin"] } }""");

        Assert.Contains("RaggableTree:Exclude", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_the_binder_cannot_convert_is_refused_by_its_key()
    {
        var error = Refusal("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "CrewFactory": { "StrictTools": "maybe" } } }""");

        Assert.Contains("Orkeon:CrewFactory:StrictTools", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_settings_file_that_is_not_JSON_is_refused_naming_the_file_and_the_line()
    {
        var error = Refusal("""
            {
              "Llm": { "Model": "gpt" ,, }
            }
            """);

        Assert.Contains(Path.Combine(_root, "appsettings.json"), error.Message, StringComparison.Ordinal);
        Assert.Contains("line 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_embedding_address_that_is_no_address_is_refused_by_its_key()
    {
        var error = Refusal("""{ "RaggableTree": { "Embedding": { "Provider": "Ollama", "BaseUrl": "pas une url" } } }""");

        Assert.Contains("RaggableTree:Embedding:BaseUrl", error.Message, StringComparison.Ordinal);
        Assert.Contains("pas une url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_OTLP_endpoint_that_is_no_address_is_refused_by_its_key()
    {
        var error = Refusal("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "OtlpEndpoint": "::" } }""");

        Assert.Contains("Telemetry:OtlpEndpoint", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40 — one line per section the runner host reads, each with a key its reader converts (or,
    /// for a section of text only, a name it checks or a key it does not carry) and a value it cannot
    /// honour: the first column is the options type the section binds, which the guard below holds
    /// against the host's own registrations. The section used to be read at its first use — a run's
    /// launch, the first RAG query, the first memory stored —, and the host was handed out.
    /// </summary>
    public static TheoryData<string, string, string, string> LateSections() => new()
    {
        { "PathSecurityOptions", "PathSecurity:MaxFileSizeBytes", "10MB", "PathSecurity:MaxFileSizeBytes" },
        { "SandboxFileSystemOptions", "Orkeon:Sandbox:CleanupOrphansOlderThan", "1 day", "Orkeon:Sandbox:CleanupOrphansOlderThan" },
        { "FileSystemOptions", "Orkeon:FileSystem:Mount:0", "/srv:/data:ro", "Orkeon:FileSystem:Mount" },
        { "RateLimitingOptions", "RateLimiting:GlobalRequestsPerMinute", "sixty", "RateLimiting:GlobalRequestsPerMinute" },
        { "GuardianOptions", "Orkeon:Guardian:Enabled", "oui", "Orkeon:Guardian:Enabled" },
        { "AuditOptions", "Security:Audit:MinSeverity", "Loud", "Security:Audit:MinSeverity" },
        { "PromptSecurityOptions", "Security:Prompt:EnableExfiltrationDetection", "perhaps", "Security:Prompt:EnableExfiltrationDetection" },
        { "ToolResultSecurityOptions", "Security:ToolResults:Policy", "Paranoid", "Security:ToolResults:Policy" },
        { "UrlSecurityOptions", "Security:Url:BlockPrivateIPs", "sure", "Security:Url:BlockPrivateIPs" },
        { "VaultOptions", "Security:Vault:CacheTtl", "an hour", "Security:Vault:CacheTtl" },
        { "CostTrackingOptions", "Orkeon:CostTracking:Enabled", "on", "Orkeon:CostTracking:Enabled" },
        { "TokenCounterOptions", "Orkeon:TokenCounter:CharsPerToken", "four", "Orkeon:TokenCounter:CharsPerToken" },
        { "ConsensualProcessOptions", "Orkeon:Consensus:MaxVotingRounds", "three", "Orkeon:Consensus:MaxVotingRounds" },
        { "SandboxOptions", "Orkeon:CodeSandbox:TimeoutSeconds", "1m", "Orkeon:CodeSandbox:TimeoutSeconds" },
        { "DockerSandboxOptions", "Orkeon:CodeSandbox:Docker:PullImageOnStartup", "daily", "Orkeon:CodeSandbox:Docker:PullImageOnStartup" },
        { "AesEncryptionOptions", "Orkeon:Encryption:KeySizeInBits", "strong", "Orkeon:Encryption:KeySizeInBits" },
        { "RedisMemoryOptions", "Orkeon:Redis:KeyPrefx", "orkeon:", "Orkeon:Redis:KeyPrefx" },
        { "SqliteMemoryOptions", "Orkeon:Sqlite:DefaultTopK", "ten", "Orkeon:Sqlite:DefaultTopK" },
        { "ChromaDbOptions", "Orkeon:ChromaDb:DefaultTopK", "ten", "Orkeon:ChromaDb:DefaultTopK" },
        { "PineconeOptions", "Orkeon:Pinecone:Environment", "us-east1-gcp", "Orkeon:Pinecone:Host" },
        { "LanceDbOptions", "Orkeon:LanceDb:EmbeddingDimension", "auto", "Orkeon:LanceDb:EmbeddingDimension" },
        { "CrewMemoryOptions", "Orkeon:CrewMemory:RecallLimit", "all", "Orkeon:CrewMemory:RecallLimit" },
        { "MemorySectionOptions", "Memory:Provider", "redsi", "Memory:Provider" },
        { "EmbeddingOptions", "Orkeon:Embeddings:Dimension", "big", "Orkeon:Embeddings:Dimension" },
        { "EmbeddingCacheOptions", "Orkeon:EmbeddingCache:SlidingExpirationMinutes", "an hour", "Orkeon:EmbeddingCache:SlidingExpirationMinutes" },
        { "CliSessionOptions", "Orkeon:Cli:Session:ContextWindowTokens", "big", "Orkeon:Cli:Session:ContextWindowTokens" },
        { "RagOptions", "Orkeon:Rag:Retrieval:TopK", "many", "Orkeon:Rag:Retrieval:TopK" },
        { "RagOptions", "Orkeon:Rag:Profile", "turbo", "Orkeon:Rag:Profile" },
        { "RagStoreOptions", "Orkeon:Rag:Provider", "mongo", "Orkeon:Rag:Provider" },
        { "RagIngestionOptions", "Orkeon:Rag:Ingestion:DefaultChunkingStrategy", "paragraph", "Orkeon:Rag:Ingestion:DefaultChunkingStrategy" },
        { "QueryRoutingOptions", "Orkeon:Rag:QueryRouting:Classifier", "smart", "Orkeon:Rag:QueryRouting:Classifier" },
        { "HybridRetrievalOptions", "Orkeon:Rag:Retrieval:Hybrid", "yes", "Orkeon:Rag:Retrieval:Hybrid" },
        { "WebSearchRetrieverOptions", "Orkeon:Rag:WebFallback:MaxResults", "three", "Orkeon:Rag:WebFallback:MaxResults" },
        { "ShellToolOptions", "Orkeon:Tools:Shell:AllowInterpreters", "yes", "Orkeon:Tools:Shell:AllowInterpreters" },
        { "ScriptingLimitsOptions", "Orkeon:Scripting:Limits:ExecutionTimeout", "2 minutes", "Orkeon:Scripting:Limits:ExecutionTimeout" },
        { "ScriptingToolchainOptions", "Orkeon:Scripting:Toolchain:EsbuildTimeout", "soon", "Orkeon:Scripting:Toolchain:EsbuildTimeout" },
        { "TelemetryOptions", "Telemetry:Enabled", "maybe", "Telemetry:Enabled" },
    };

    /// <summary>The settings of a case: one key over a file that otherwise builds, the RaggableTree off (no model loads).</summary>
    private string OneSetting(string key, string value) =>
        Settings(SettingsJson.Of(new Dictionary<string, string> { ["RaggableTree:Enabled"] = "false", [key] = value }));

    /// <summary>The host of the theory: one mount, so the file system — and its sections — is registered.</summary>
    private RunnerMountPlan Mounted() => new() { CliMounts = [$"{_root}:/workspace:ro"], AllowExternalMounts = true };

    [Theory]
    [MemberData(nameof(LateSections))]
    public void Every_section_the_host_reads_is_judged_at_its_start(string optionsType, string key, string value, string named)
    {
        _ = optionsType;
        var settings = OneSetting(key, value);

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, Mounted()));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard of the theory: every Orkeon options type the runner host binds from its settings — an
    /// <see cref="IOptionsChangeTokenSource{TOptions}"/> registered, or a section declared with its
    /// options — has its line above. A section a registration starts to bind without its line fails here.
    /// </summary>
    [Fact]
    public void Every_bound_section_of_the_runner_host_has_its_line_in_the_theory()
    {
        IServiceCollection? captured = null;
        using (RunnerHost.Build(OneSetting("RaggableTree:Enabled", "false"), Mounted(),
                   configureServices: (_, services) => captured = services))
        {
        }

        var tokenSources = captured!
            .Where(descriptor => descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IOptionsChangeTokenSource<>))
            .Select(descriptor => descriptor.ServiceType.GetGenericArguments()[0]);
        var declared = captured!
            .Where(descriptor => descriptor.ServiceType == typeof(SettingsDeclaration))
            .Select(descriptor => (SettingsDeclaration)descriptor.ImplementationInstance!)
            .Where(declaration => declaration.Evaluate is not null)
            .Select(declaration => declaration.Shape);
        var bound = tokenSources.Concat(declared)
            .Where(type => type.Namespace?.StartsWith("Orkeon.", StringComparison.Ordinal) == true)
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        var lines = LateSections().Select(row => row.Data.Item1).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(bound.Except(lines));
    }

    /// <summary>
    /// GAP-40 — <c>Orkeon:FileSystem</c> and <c>Orkeon:Sandbox</c> are judged whether the host mounts
    /// anything or not. A run always mounts — its crew, its output —, but <c>orkeon doctor</c> builds
    /// the host without a mount, and said all green on a file the run refused.
    /// </summary>
    [Theory]
    [InlineData("Orkeon:Sandbox:CleanupOrphansOlderThan", "1 day", "Orkeon:Sandbox:CleanupOrphansOlderThan")]
    [InlineData("Orkeon:FileSystem:Mount:0", "/srv:/data:ro", "Orkeon:FileSystem:Mount")]
    public void The_file_system_sections_are_judged_when_nothing_is_mounted(string key, string value, string named)
    {
        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(OneSetting(key, value), new RunnerMountPlan()));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40 — a number the binder reads as a <c>double</c> or a <c>float</c> must be finite: it
    /// takes <c>"NaN"</c>, <c>"Infinity"</c> and an overflow (<c>1e40</c> for a float) without a word.
    /// A RAG generation temperature then failed every request it reached, JSON writing no such number;
    /// a minimum score matched nothing, and the crew recalled nothing.
    /// </summary>
    [Theory]
    [InlineData("Orkeon:Rag:Generation:Temperature", "NaN")]
    [InlineData("Orkeon:CrewMemory:MinScore", "Infinity")]
    [InlineData("Orkeon:Sqlite:MinSimilarityScore", "1e40")]
    [InlineData("Orkeon:Consensus:RoleWeights:Reviewer", "-Infinity")]
    public void A_number_that_is_not_finite_is_refused_by_its_key(string key, string value)
    {
        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(OneSetting(key, value), Mounted()));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.Contains("finite", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-40 — only what the binder, a rule, the names or the keys refuse is a refused setting: a
    /// defect of the code that evaluates a section keeps its type and its stack, as the caller's own
    /// exceptions do (GAP-35). It became a refusal, worded as the operator's to fix.
    /// </summary>
    [Fact]
    public void A_defect_of_the_code_evaluating_a_section_keeps_its_type()
    {
        var error = Assert.ThrowsAny<InvalidOperationException>(() => RunnerHost.Build(
            OneSetting("RaggableTree:Enabled", "false"),
            new RunnerMountPlan(),
            configureServices: (_, services) => services.AddOptions<DefectiveOptions>()
                .Configure(_ => throw new InvalidOperationException("a defect, not a setting"))
                .DeclareSettings("MyApp:Defective")));

        Assert.IsNotType<RunnerSettingsException>(error);
        Assert.Equal("a defect, not a setting", error.Message);
    }

    /// <summary>The options of a section whose setup is broken.</summary>
    public sealed class DefectiveOptions
    {
        public bool On { get; set; }
    }

    [Fact]
    public void A_file_without_any_of_these_sections_builds_the_host_as_before()
    {
        using var host = RunnerHost.Build(OneSetting("RaggableTree:Enabled", "false"), Mounted());

        Assert.NotNull(host);
    }

    /// <summary>
    /// GAP-40, decision 3 — <c>Logging</c> is read while the host builds its logger, before any
    /// barrier: a level it does not know made the build throw a bare exception, without the key.
    /// </summary>
    [Theory]
    [InlineData("Logging:LogLevel:Orkeon", "Informations")]
    [InlineData("Logging:Console:LogLevel:Default", "Loud")]
    [InlineData("Logging:Console:LogToStandardErrorThreshold", "all")]
    public void A_logging_setting_that_is_none_is_refused_by_its_key(string key, string value)
    {
        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(OneSetting(key, value), new RunnerMountPlan()));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_logging_level_the_logging_configuration_knows_is_accepted_whatever_its_case()
    {
        using var host = RunnerHost.Build(OneSetting("Logging:LogLevel:MyNamespace", "warning"), new RunnerMountPlan());

        Assert.NotNull(host);
    }

    /// <summary>
    /// GAP-40, decision 4 — the default <c>Llm</c> section is read as strictly as a profile: a number
    /// or a switch it cannot read refuses the start, where it used to fall back on the default.
    /// </summary>
    [Theory]
    [InlineData("Llm:TimeoutSeconds", "600s")]
    [InlineData("Llm:Temperature", "warm")]
    [InlineData("Llm:Thinking:Enabled", "yes")]
    [InlineData("Llm:Grammar", "yes")]
    [InlineData("Llm:Profiles:a:Grammar", "yes")]
    [InlineData("Llm:Temperature", "Infinity")]
    [InlineData("Llm:Profiles:a:Temperature", "NaN")]
    public void A_swallowed_Llm_value_is_refused_by_its_key(string key, string value)
    {
        var settings = Settings(SettingsJson.Of(new Dictionary<string, string>
        {
            ["RaggableTree:Enabled"] = "false",
            ["Llm:BaseUrl"] = "http://localhost:11434",
            ["Llm:Profiles:a:BaseUrl"] = "http://localhost:11434",
            [key] = value,
        }));

        var error = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, new RunnerMountPlan()));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "ExportToConsole": true } }""", "Telemetry:ExportToConsole")]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "ExportToConsole": false } }""", "Telemetry:ExportToConsole")]
    [InlineData("""{ "RaggableTree": { "Enabled": false }, "Telemetry": { "PrometheusEndpoint": true } }""", "Telemetry:PrometheusEndpoint")]
    public void A_removed_telemetry_key_is_refused_naming_what_replaces_it(string json, string key)
    {
        var error = Refusal(json);

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.Contains("Telemetry:OtlpEndpoint", error.Message, StringComparison.Ordinal);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", error.Message, StringComparison.Ordinal);
    }
}
