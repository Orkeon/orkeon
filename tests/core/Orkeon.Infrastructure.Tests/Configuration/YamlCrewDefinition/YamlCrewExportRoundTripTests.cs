using System.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-39: a crew loaded, exported and loaded again is the same crew — everything the loader reads, under the
/// keys its author wrote, and no key nobody set. The configurations are compared by
/// <see cref="CrewConfigurationProjection"/>. Two witnesses (<c>TestData/round-trip/</c>) set every key of the
/// YAML models between them — the completeness guard holds them to it, so a key added to a model fails here
/// until a witness sets it, and the round trip then makes the export write it —; every crew of
/// <c>examples/</c> makes the trip too.
/// </summary>
public partial class YamlCrewExportRoundTripTests
{
    private static readonly string[] WitnessFiles = ["sequential.yaml", "hierarchical.yaml"];

    private readonly YamlDotNetSerializer _serializer = new();
    private readonly FakeFileSystemService _fs = new();
    private readonly YamlCrewDefinitionLoader _loader;
    private readonly YamlCrewExporter _exporter;

    public YamlCrewExportRoundTripTests()
    {
        _loader = new YamlCrewDefinitionLoader(_serializer, _fs, NullLogger<YamlCrewDefinitionLoader>.Instance);
        _exporter = new YamlCrewExporter(_serializer, _fs, NullLogger<YamlCrewExporter>.Instance);
    }

    public static TheoryData<string> Witnesses => [.. WitnessFiles];

    #region The witnesses and the completeness guard

    [Theory]
    [MemberData(nameof(Witnesses))]
    public async Task A_witness_is_a_crew_the_validator_accepts(string witness)
    {
        var config = await LoadWitnessAsync(witness);

        var result = CrewDefinitionValidator.Validate(config);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task The_witnesses_set_every_key_the_yaml_models_read()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var witness in WitnessFiles)
            set.UnionWith(YamlModelPaths.SetIn(await ReadWitnessModelAsync(witness)));

        var missing = YamlModelPaths.Of(typeof(CrewYamlConfig)).Except(set, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "No round-trip witness sets " + string.Join(", ", missing) + ". Set each key in "
            + "TestData/round-trip/sequential.yaml or hierarchical.yaml: the round trip then proves the export writes it.");
    }

    /// <summary>
    /// <c>knowledge</c> is a list of untyped items the guard cannot see into: a name (the short form) or a
    /// table (the long form), whose keys are the properties of <see cref="KnowledgeAttachment"/>.
    /// </summary>
    [Fact]
    public async Task The_witnesses_write_knowledge_in_both_forms_with_every_long_form_key()
    {
        var shortForms = 0;
        var longFormKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var witness in WitnessFiles)
        {
            var model = await ReadWitnessModelAsync(witness);
            foreach (var item in model.Agents!.Values.SelectMany(agent => agent.Knowledge ?? []))
            {
                if (item is string)
                    shortForms++;
                else if (item is IDictionary table)
                    longFormKeys.UnionWith(table.Keys.Cast<object>().Select(key => Normalized(key.ToString()!)));
            }
        }

        var expected = typeof(KnowledgeAttachment).GetProperties()
            .Where(property => property.CanWrite)
            .Select(property => Normalized(property.Name))
            .ToList();

        Assert.True(shortForms > 0, "No witness attaches knowledge in its short form.");
        Assert.All(expected, key => Assert.Contains(key, longFormKeys));
    }

    /// <summary>The directory layout reads its crew file as <see cref="CrewSettingsYamlConfig"/>: every crew-level key.</summary>
    [Fact]
    public void The_crew_settings_file_reads_every_crew_level_key_of_the_single_file()
    {
        var single = YamlModelPaths.KeysOf(typeof(CrewYamlConfig))
            .Where(entry => entry.Key is not ("agents" or "tasks"))
            .Select(entry => $"{entry.Key}: {entry.Property.PropertyType}");
        var settings = YamlModelPaths.KeysOf(typeof(CrewSettingsYamlConfig))
            .Select(entry => $"{entry.Key}: {entry.Property.PropertyType}");

        Assert.Equal(single.Order(StringComparer.Ordinal), settings.Order(StringComparer.Ordinal));
    }

    #endregion

    #region The round trip

    [Theory]
    [MemberData(nameof(Witnesses))]
    public async Task A_loaded_crew_exported_and_reloaded_is_the_same_crew(string witness)
    {
        var loaded = await LoadWitnessAsync(witness);

        var reloaded = await _loader.LoadFromStringAsync(_exporter.ExportToString(loaded), TestContext.Current.CancellationToken);

        CrewConfigurationProjection.AssertEqual(loaded, reloaded);
    }

    [Theory]
    [MemberData(nameof(Witnesses))]
    public async Task The_export_of_a_reloaded_crew_is_the_same_text_byte_for_byte(string witness)
    {
        var loaded = await LoadWitnessAsync(witness);
        var first = _exporter.ExportToString(loaded);

        var reloaded = await _loader.LoadFromStringAsync(first, TestContext.Current.CancellationToken);

        Assert.Equal(first, _exporter.ExportToString(reloaded));
    }

    [Theory]
    [MemberData(nameof(Witnesses))]
    public async Task A_crew_exported_to_a_directory_reads_back_the_same(string witness)
    {
        var loaded = await LoadWitnessAsync(witness);

        await _exporter.ExportToDirectoryAsync(loaded, "/out/crew", TestContext.Current.CancellationToken);
        var reloaded = await _loader.LoadFromDirectoryAsync("/out/crew", TestContext.Current.CancellationToken);

        CrewConfigurationProjection.AssertEqual(loaded, reloaded);
    }

    /// <summary>
    /// An empty block is a declaration: <c>links: []</c> closes the EventHub door, where an absent block leaves the
    /// host's policy to decide. Each comes back as it was.
    /// </summary>
    [Fact]
    public async Task An_empty_block_comes_back_empty_and_an_absent_one_absent()
    {
        var empty = await _loader.LoadFromStringAsync(
            """
            name: empties
            goal: Declare empty blocks
            links: []
            mounts: []
            rag: {}
            llm: {}
            agents:
              worker:
                goal: Work
            tasks:
              job:
                description: Do the job
                expectedOutput: The job done
            """,
            TestContext.Current.CancellationToken);
        var absent = await _loader.LoadFromStringAsync(
            """
            name: absents
            goal: Declare no block
            agents:
              worker:
                goal: Work
            tasks:
              job:
                description: Do the job
                expectedOutput: The job done
            """,
            TestContext.Current.CancellationToken);

        var emptyAgain = await _loader.LoadFromStringAsync(_exporter.ExportToString(empty), TestContext.Current.CancellationToken);
        var absentAgain = await _loader.LoadFromStringAsync(_exporter.ExportToString(absent), TestContext.Current.CancellationToken);

        Assert.Empty(Assert.IsType<IReadOnlyList<Orkeon.Domain.EventHub.CrewLink>>(emptyAgain.Links, exactMatch: false));
        Assert.Empty(Assert.IsType<IReadOnlyList<Orkeon.Domain.FileSystem.MountReference>>(emptyAgain.Mounts, exactMatch: false));
        Assert.Empty(Assert.IsType<RagCrewConfig>(emptyAgain.Rag).Collections);
        Assert.NotNull(Assert.Single(emptyAgain.Agents).LlmConfig);
        CrewConfigurationProjection.AssertEqual(empty, emptyAgain);

        Assert.Null(absentAgain.Links);
        Assert.Null(absentAgain.Mounts);
        Assert.Null(absentAgain.Rag);
        Assert.Null(Assert.Single(absentAgain.Agents).LlmConfig);
        CrewConfigurationProjection.AssertEqual(absent, absentAgain);
    }

    /// <summary>
    /// Decision 1: the export writes what the configuration carries. A preset comes back as its rules, the crew's
    /// <c>llm:</c> under each agent, an anchor as the text it stood for.
    /// </summary>
    [Fact]
    public async Task The_export_writes_a_preset_as_its_rules_the_crew_llm_under_each_agent_and_anchors_expanded()
    {
        var loaded = await LoadWitnessAsync("sequential.yaml");

        var yaml = _exporter.ExportToString(loaded);
        var exported = _serializer.Deserialize<CrewYamlConfig>(yaml);

        var researcher = exported.Agents!["researcher"];
        var guardrails = Assert.IsType<GuardrailsYamlConfig>(researcher.Guardrails);
        Assert.Null(guardrails.Preset);
        Assert.Equal(GuardrailPresets.Analysis.Header, guardrails.Header);
        Assert.Equal([.. GuardrailPresets.Analysis.Rules, "Cite the page each finding comes from."], guardrails.Rules!);
        Assert.Equal(["file_write", "directory_read", "web_search"], guardrails.ToolRules!.Keys);

        Assert.Null(exported.Llm);
        Assert.Equal("fast", exported.Agents["writer"].Llm!.Profile);
        Assert.Equal(0.2, exported.Agents["writer"].Llm!.Temperature);
        Assert.Equal("low", researcher.Llm!.Thinking!.Effort);

        Assert.DoesNotContain("anchors:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("*sourcing", yaml, StringComparison.Ordinal);
        Assert.Contains("Every finding names the page it comes from.", researcher.Backstory, StringComparison.Ordinal);
    }

    #endregion

    #region The author's keys

    [Fact]
    public async Task The_export_of_a_loaded_crew_writes_its_authors_keys()
    {
        var sequential = _serializer.Deserialize<CrewYamlConfig>(_exporter.ExportToString(await LoadWitnessAsync("sequential.yaml")));
        var hierarchical = _serializer.Deserialize<CrewYamlConfig>(_exporter.ExportToString(await LoadWitnessAsync("hierarchical.yaml")));

        Assert.Equal(["researcher", "writer"], sequential.Agents!.Keys);
        Assert.Equal(["collect", "report"], sequential.Tasks!.Keys);
        Assert.Equal("researcher", sequential.Tasks["collect"].Agent);
        Assert.Equal(["collect"], sequential.Tasks["report"].Dependencies!);
        Assert.Equal("lead", hierarchical.ManagerAgent);
        Assert.Equal(["review"], hierarchical.Tasks!["decide"].Dependencies!);
    }

    [Fact]
    public void A_configuration_built_in_code_is_exported_under_its_identifiers()
    {
        var lead = AgentId.Create();
        var plan = TaskId.Create();
        var execute = TaskId.Create();
        var config = new CrewConfiguration
        {
            Name = "coded",
            Goal = "Built in code",
            Process = ProcessType.Hierarchical,
            ManagerAgentId = lead,
            Agents = [new AgentConfiguration { Id = lead, Role = "Lead", Goal = "Lead" }],
            Tasks =
            [
                new TaskConfiguration { Id = plan, Description = "Plan", ExpectedOutput = "A plan", AssignedAgentId = lead },
                new TaskConfiguration { Id = execute, Description = "Execute", ExpectedOutput = "Done", Dependencies = [plan] },
            ],
        };

        var exported = _serializer.Deserialize<CrewYamlConfig>(_exporter.ExportToString(config));

        Assert.Equal([lead.ToString()], exported.Agents!.Keys);
        Assert.Equal([plan.ToString(), execute.ToString()], exported.Tasks!.Keys);
        Assert.Equal(lead.ToString(), exported.Tasks[plan.ToString()].Agent);
        Assert.Equal([plan.ToString()], exported.Tasks[execute.ToString()].Dependencies!);
        Assert.Equal(lead.ToString(), exported.ManagerAgent);
    }

    [Fact]
    public void Two_agents_under_one_key_are_refused_at_export()
    {
        var config = new CrewConfiguration
        {
            Name = "doubled",
            Goal = "Two agents, one key",
            Agents =
            [
                new AgentConfiguration { Key = "analyst", Role = "First analyst", Goal = "Analyse" },
                new AgentConfiguration { Key = "analyst", Role = "Second analyst", Goal = "Analyse" },
            ],
            Tasks = [new TaskConfiguration { Key = "job", Description = "Analyse", ExpectedOutput = "An analysis" }],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _exporter.ExportToString(config));

        Assert.Contains("'analyst'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_naming_no_agent_of_the_crew_is_refused_at_export()
    {
        var stranger = AgentId.Create();
        var config = new CrewConfiguration
        {
            Name = "dangling",
            Goal = "A reference to nobody",
            Agents = [new AgentConfiguration { Key = "worker", Role = "Worker", Goal = "Work" }],
            Tasks = [new TaskConfiguration { Key = "collect", Description = "Collect", ExpectedOutput = "Data", AssignedAgentId = stranger }],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _exporter.ExportToString(config));

        Assert.Contains("'collect'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(stranger.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dependency_and_a_manager_naming_nothing_are_refused_at_export_together()
    {
        var ghostTask = TaskId.Create();
        var ghostManager = AgentId.Create();
        var config = new CrewConfiguration
        {
            Name = "dangling",
            Goal = "References to nothing",
            Process = ProcessType.Hierarchical,
            ManagerAgentId = ghostManager,
            Agents = [new AgentConfiguration { Key = "worker", Role = "Worker", Goal = "Work" }],
            Tasks = [new TaskConfiguration { Key = "report", Description = "Report", ExpectedOutput = "A report", Dependencies = [ghostTask] }],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _exporter.ExportToString(config));

        Assert.Contains(ghostTask.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains(ghostManager.ToString(), ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region No key nobody set

    [Fact]
    public async Task The_export_of_a_minimal_crew_writes_no_empty_key()
    {
        var config = await _loader.LoadFromStringAsync(
            """
            name: minimal
            goal: Do one thing
            agents:
              worker:
                goal: Work
            tasks:
              job:
                description: Do the job
                expectedOutput: The job done
            """,
            TestContext.Current.CancellationToken);

        var empty = EmptyKeysOf(_exporter.ExportToString(config));

        Assert.True(empty.Count == 0, "The export writes empty keys: " + string.Join(", ", empty));
    }

    [Theory]
    [MemberData(nameof(Witnesses))]
    public async Task The_export_of_a_witness_writes_no_empty_key(string witness)
    {
        var empty = EmptyKeysOf(_exporter.ExportToString(await LoadWitnessAsync(witness)));

        Assert.True(empty.Count == 0, "The export writes empty keys: " + string.Join(", ", empty));
    }

    #endregion

    #region The validator names an entry by its key

    [Fact]
    public async Task The_validator_names_an_agent_and_a_task_of_a_loaded_crew_by_their_keys()
    {
        var config = await _loader.LoadFromStringAsync(
            """
            name: keyed
            goal: Name entries by their keys
            agents:
              researcher:
                role: Researcher
            tasks:
              collect:
                agent: researcher
            """,
            TestContext.Current.CancellationToken);

        var errors = CrewDefinitionValidator.Validate(config).Errors;

        Assert.Contains("Agent 'researcher' must have a goal.", errors);
        Assert.Contains("Task 'collect' must have a description.", errors);
        Assert.Contains("Task 'collect' must have an expected output.", errors);
    }

    [Fact]
    public void The_validator_names_an_entry_built_in_code_by_its_identifier()
    {
        var agent = new AgentConfiguration { Role = "Researcher" };
        var task = new TaskConfiguration { ExpectedOutput = "Data" };

        var errors = CrewDefinitionValidator.Validate(
            new CrewConfiguration { Name = "coded", Goal = "Built in code", Agents = [agent], Tasks = [task] }).Errors;

        Assert.Contains($"Agent '{agent.Id}' must have a goal.", errors);
        Assert.Contains($"Task '{task.Id}' must have a description.", errors);
    }

    [Fact]
    public void The_validator_names_a_task_by_its_key_next_to_the_identifier_it_cannot_resolve()
    {
        var stranger = AgentId.Create();
        var ghost = TaskId.Create();
        var config = new CrewConfiguration
        {
            Name = "keyed",
            Goal = "Keys set in code",
            Agents = [new AgentConfiguration { Key = "worker", Role = "Worker", Goal = "Work" }],
            Tasks = [new TaskConfiguration { Key = "collect", Description = "Collect", ExpectedOutput = "Data", AssignedAgentId = stranger, Dependencies = [ghost] }],
        };

        var errors = CrewDefinitionValidator.Validate(config).Errors;

        Assert.Contains($"Task 'collect' references unknown agent '{stranger}'.", errors);
        Assert.Contains($"Task 'collect' references unknown dependency '{ghost}'.", errors);
    }

    #endregion

    #region The projection itself

    [Fact]
    public async Task The_projection_names_the_path_of_a_difference()
    {
        var loaded = await LoadWitnessAsync("sequential.yaml");
        var changed = loaded with
        {
            Tasks = [.. loaded.Tasks.Select(task => task.Key == "collect"
                ? task with { Deliverable = task.Deliverable! with { SchemaInline = """{"type":"object"}""" } }
                : task)],
        };

        var difference = Assert.Single(CrewConfigurationProjection.Differences(loaded, changed));

        Assert.StartsWith("Tasks[collect].Deliverable.SchemaInline:", difference, StringComparison.Ordinal);
    }

    #endregion

    #region Helpers

    private static string WitnessPath(string witness) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "round-trip", witness);

    private static Task<string> ReadWitnessAsync(string witness) =>
        File.ReadAllTextAsync(WitnessPath(witness), TestContext.Current.CancellationToken);

    private async Task<CrewConfiguration> LoadWitnessAsync(string witness) =>
        await _loader.LoadFromStringAsync(await ReadWitnessAsync(witness), TestContext.Current.CancellationToken);

    /// <summary>A witness as the serializer reads it — anchors expanded first, as the loader does.</summary>
    private async Task<CrewYamlConfig> ReadWitnessModelAsync(string witness) =>
        _serializer.Deserialize<CrewYamlConfig>(YamlAnchorPreprocessor.Preprocess(await ReadWitnessAsync(witness)));

    private static string Normalized(string key) =>
        key.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>Each key of <paramref name="yaml"/> whose value is a null node — <c>backstory:</c> with nothing after it.</summary>
    private static List<string> EmptyKeysOf(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        var empty = new List<string>();
        foreach (var document in stream.Documents)
            CollectEmptyKeys(document.RootNode, string.Empty, empty);
        return empty;
    }

    private static void CollectEmptyKeys(YamlNode node, string path, List<string> empty)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (key, value) in mapping.Children)
                {
                    var name = (key as YamlScalarNode)?.Value ?? key.ToString();
                    var keyPath = path.Length == 0 ? name : $"{path}.{name}";
                    if (IsNullNode(value))
                        empty.Add(keyPath);
                    else
                        CollectEmptyKeys(value, keyPath, empty);
                }
                break;
            case YamlSequenceNode sequence:
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    if (IsNullNode(sequence.Children[i]))
                        empty.Add($"{path}[{i}]");
                    else
                        CollectEmptyKeys(sequence.Children[i], $"{path}[{i}]", empty);
                }
                break;
        }
    }

    private static bool IsNullNode(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar
        && (string.IsNullOrEmpty(scalar.Value) || scalar.Value is "~" or "null" or "Null" or "NULL");

    #endregion
}
