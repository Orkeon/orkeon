using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using System.Reflection;
using Orkeon.Infrastructure.Serialization;
using YamlDotNet.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// CH-01: a crew YAML key no model reads refuses the load, naming the file, the line, the path of
/// the key and the closest known key, in the three layouts. Real files through the disk-backed VFS.
/// </summary>
public sealed class UnknownCrewYamlKeysTests : IDisposable
{
    private const string VirtualRoot = "/crew";

    private readonly string _physicalRoot = Path.Combine(
        Path.GetTempPath(), "ork-unknownkeys-" + Guid.NewGuid().ToString("N"));

    public UnknownCrewYamlKeysTests() => Directory.CreateDirectory(_physicalRoot);

    public void Dispose()
    {
        try { Directory.Delete(_physicalRoot, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_physicalRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private YamlCrewDefinitionLoader NewLoader()
        => new(
            new YamlDotNetSerializer(),
            new DiskBackedFileSystemService(_physicalRoot, VirtualRoot),
            NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static string Crew(string root = "", string agent = "", string task = "") =>
        "name: c\ngoal: g\n"
        + (root.Length > 0 ? root + "\n" : string.Empty)
        + "agents:\n  researcher:\n    role: Researcher\n    goal: G\n"
        + (agent.Length > 0 ? agent + "\n" : string.Empty)
        + "tasks:\n  t:\n    description: D\n    agent: researcher\n"
        + (task.Length > 0 ? task + "\n" : string.Empty);

    private static int LineOf(string yaml, string key)
        => Array.FindIndex(yaml.Split('\n'), l => l.TrimStart().StartsWith(key + ":", StringComparison.Ordinal)) + 1;

    private async Task<string> RefusalOfFileAsync(string yaml)
    {
        Write("crew.yaml", yaml);
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewLoader().LoadFromFileAsync(VirtualRoot + "/crew.yaml", TestContext.Current.CancellationToken));
        return ex.Message;
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldRefuseTheCrew_WhenAnAgentLlmKeyIsMisspelled()
    {
        var yaml = Crew(agent: "    llm:\n      model: gpt-4o\n      temprature: 0.2");

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("crew.yaml", message);
        Assert.Contains(LineOf(yaml, "temprature").ToString(), message);
        Assert.Contains("agents[researcher].llm.temprature", message);
        Assert.Contains("temperature", message);
    }

    [Theory]
    [InlineData("proces: sequential", "", "", "proces", "proces", "process")]
    [InlineData("", "    Backstory: b", "", "Backstory", "agents[researcher].Backstory", "backstory")]
    [InlineData("", "    temperature: 0.2", "", "temperature", "agents[researcher].temperature", "llm.temperature")]
    [InlineData("", "    llm:\n      thinking:\n        efort: max", "", "efort", "agents[researcher].llm.thinking.efort", "effort")]
    [InlineData("", "", "    expected_ouput: o", "expected_ouput", "tasks[t].expected_ouput", "expected_output")]
    [InlineData("", "", "    llm_override:\n      temprature: 0.1", "temprature", "tasks[t].llm_override.temprature", "temperature")]
    [InlineData("", "", "    deliverable:\n      pth: out.md", "pth", "tasks[t].deliverable.pth", "path")]
    [InlineData("", "    guardrails:\n      rule: [x]", "", "rule", "agents[researcher].guardrails.rule", "rules")]
    [InlineData("graphConfig:\n  maxTransition: 3", "", "", "maxTransition", "graphConfig.maxTransition", "maxTransitions")]
    [InlineData("rag:\n  collections:\n    docs:\n      sources: [a]\n      chunking: { max_token: 5 }", "", "", "chunking", "rag.collections[docs].chunking.max_token", "max_tokens")]
    [InlineData("links:\n  - to: x\n    allowed_topic: [a]", "", "", "allowed_topic", "links[0].allowed_topic", "allowed_topics")]
    [InlineData("", "    knowledge:\n      - docs\n      - collection: docs\n        top_kk: 3", "", "top_kk", "agents[researcher].knowledge[1].top_kk", "top_k")]
    [InlineData("inputs:\n  q: x", "", "", "inputs", "inputs", null)]
    [Trait("BR", "CH-01")]
    public async Task ShouldRefuseEachLevel_WhenAKeyIsUnknown(
        string root, string agent, string task, string key, string path, string? proposal)
    {
        var yaml = Crew(root, agent, task);

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains(path, message);
        Assert.Contains(LineOf(yaml, key).ToString(), message);
        if (proposal is null)
        {
            Assert.DoesNotContain("Did you mean", message);
            Assert.Contains("goal", message);
            Assert.Contains("agents", message);
        }
        else
        {
            Assert.Contains(proposal, message);
        }
    }

    [Theory]
    [InlineData("", "    expected_ouput: o", "expected_ouput", "expected_output", "")]
    [InlineData("", "    expectedOuput: o", "expectedOuput", "expectedOutput", "")]
    [InlineData("    Backstory: b", "", "Backstory", "backstory", "keys are case-sensitive")]
    [Trait("BR", "CH-01")]
    public async Task ShouldFollowTheAuthorsDialect_WhenProposingAKey(string agent, string task, string key, string proposal, string phrase)
    {
        var message = await RefusalOfFileAsync(Crew(agent: agent, task: task));

        Assert.Contains(key, message);
        Assert.Contains(proposal, message);
        Assert.Contains(phrase, message);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldRefuseOnceWithEveryFault_WhenTwoFilesHoldUnknownKeys()
    {
        // Flat folder
        Write("flat/crew.yaml", "name: c\ngoal: g\n");
        Write("flat/agents.yaml", "researcher:\n  role: R\n  goal: G\n  temprature: 1\n");
        Write("flat/tasks.yaml", "t:\n  description: D\n  expected_ouput: o\n  agent: researcher\n");

        var flat = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewLoader().LoadFromDirectoryAsync(VirtualRoot + "/flat", TestContext.Current.CancellationToken));

        Assert.Contains("agents.yaml", flat.Message);
        Assert.Contains("tasks.yaml", flat.Message);
        Assert.Contains("temprature", flat.Message);
        Assert.Contains("expected_ouput", flat.Message);

        // Folder per entity
        Write("per/config.yaml", "name: c\ngoal: g\n");
        Write("per/agents/a.yaml", "role: R\ngoal: G\ntemprature: 1\n");
        Write("per/tasks/t.yaml", "description: D\nexpected_ouput: o\nagent: a\n");

        var per = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewLoader().LoadFromDirectoryAsync(VirtualRoot + "/per", TestContext.Current.CancellationToken));

        Assert.Contains("a.yaml", per.Message);
        Assert.Contains("t.yaml", per.Message);
        Assert.Contains("temprature", per.Message);
        Assert.Contains("expected_ouput", per.Message);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldRefuseAgentsInCrewSettings_WhenAFolderCrewWritesThem()
    {
        Write("settings/config.yaml", "name: c\ngoal: g\nagents:\n  a:\n    role: R\n    goal: G\n");
        Write("settings/agents/b.yaml", "role: R\ngoal: G\n");

        var inSettings = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewLoader().LoadFromDirectoryAsync(VirtualRoot + "/settings", TestContext.Current.CancellationToken));

        Assert.Contains("config.yaml", inSettings.Message);
        Assert.Contains("agents", inSettings.Message);

        Write("wrapped/config.yaml", "name: c\ngoal: g\n");
        Write("wrapped/agents/a.yaml", "a:\n  role: R\n  goal: G\n");

        var wrapped = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewLoader().LoadFromDirectoryAsync(VirtualRoot + "/wrapped", TestContext.Current.CancellationToken));

        Assert.Contains("a.yaml", wrapped.Message);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseWithTheBlockPhrase_WhenLlmIsAPlainString()
    {
        var yaml = Crew(agent: "    llm: gpt-4o");

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("llm is a block", message);
        Assert.Contains("model:", message);
        Assert.Contains("agents[researcher].llm", message);
        Assert.Contains("crew.yaml", message);
        Assert.Contains(LineOf(yaml, "llm").ToString(), message);

        var taskYaml = Crew(task: "    context: [a]");
        var taskMessage = await RefusalOfFileAsync(taskYaml);

        Assert.Contains("dependencies:", taskMessage);
        Assert.Contains("tasks[t].context", taskMessage);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseOutputFile_WhenACrewAIKeyIsWritten()
    {
        var yaml = Crew(task: "    output_file: out.md");

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("tasks[t].output_file", message);
        Assert.Contains(LineOf(yaml, "output_file").ToString(), message);
        Assert.Contains("is retired", message);
        Assert.Contains("deliverable:", message);
        Assert.DoesNotContain("Did you mean", message);
        Assert.DoesNotContain("unknown key", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseTheDuplicate_WhenAnAgentKeyIsWrittenTwice()
    {
        var yaml = "name: c\ngoal: g\nagents:\n  researcher:\n    role: R\n    goal: G\n"
            + "  researcher:\n    role: R2\n    goal: G2\n"
            + "tasks:\n  t:\n    description: D\n    agent: researcher\n";

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("duplicate", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("researcher", message);
        Assert.Contains("crew.yaml", message);
        Assert.Contains("line 7", message);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseTheMergeKey_WhenACrewWritesIt()
    {
        var yaml = "name: c\ngoal: g\nagents:\n  base: &base\n    role: R\n    goal: G\n"
            + "  researcher:\n    <<: *base\n    role: R2\n"
            + "tasks:\n  t:\n    description: D\n    agent: researcher\n";

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("merge key", message);
        Assert.Contains("not supported", message);
        Assert.Contains("agents[researcher]", message);
        Assert.Contains("line 8", message);

        var rootYaml = Crew(root: "defaults: &d\n  maxIter: 3");

        var rootMessage = await RefusalOfFileAsync(rootYaml);

        Assert.Contains("anchors:", rootMessage);
        Assert.Contains("defaults", rootMessage);
        Assert.Contains(LineOf(rootYaml, "defaults").ToString(), rootMessage);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldJudgeAnAliasWhereItIsUsed_WhenOneAnchorServesTwoModels()
    {
        var yaml = "name: c\ngoal: g\nagents:\n"
            + "  researcher:\n    role: R\n    goal: G\n    llm: &llm_base\n      model: gpt-4o\n      temprature: 0.2\n"
            + "  writer:\n    role: W\n    goal: G\n    llm: *llm_base\n"
            + "tasks:\n  t:\n    description: D\n    agent: researcher\n    llm_override: *llm_base\n";

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("agents[researcher].llm.temprature", message);
        Assert.Contains("line 9", message);
        Assert.DoesNotContain("agents[writer].llm.temprature", message);
        Assert.Contains("tasks[t].llm_override.model", message);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCiteTheFileLine_WhenAnAnchorsSectionPrecedesTheFault()
    {
        var yaml = "anchors:\n  tone: |\n    Be brief.\n    Be kind.\n    Be exact.\n"
            + Crew(root: "proces: sequential");

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("proces", message);
        Assert.Contains("line " + LineOf(yaml, "proces"), message);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldNameFileAndLine_WhenAValueDoesNotConvert()
    {
        var yaml = Crew(agent: "    maxIter: dix");

        var message = await RefusalOfFileAsync(yaml);

        Assert.Contains("crew.yaml", message);
        Assert.Contains("line " + LineOf(yaml, "maxIter"), message);
        Assert.Contains("dix", message);
    }

    private static readonly Type[] s_models = ModelsFrom(typeof(CrewYamlConfig));

    private static Type[] ModelsFrom(Type root)
    {
        var seen = new List<Type>();
        void Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    Visit(argument);
                return;
            }

            if (type.IsArray)
            {
                Visit(type.GetElementType()!);
                return;
            }

            if (type.Namespace != root.Namespace || !type.Name.EndsWith("YamlConfig", StringComparison.Ordinal) || seen.Contains(type))
                return;
            seen.Add(type);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Visit(property.PropertyType);
        }

        Visit(root);
        return [.. seen];
    }

    private static string Snake(string name)
        => string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static IReadOnlyList<string> Faults(Type model, string yaml)
        => CrewYamlKeyCheck.Find(new ExpandedYaml(yaml, null), "/crew/x.yaml", model, string.Empty);

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldReadAYmlEntityFile_WhenAFolderCrewWritesOne()
    {
        Write("yml/config.yaml", "name: c\ngoal: g\n");
        Write("yml/agents/writer.yml", "role: R\ngoal: G\n");
        Write("yml/tasks/t.yaml", "description: D\nagent: writer\n");

        var crew = await NewLoader().LoadFromDirectoryAsync(VirtualRoot + "/yml", TestContext.Current.CancellationToken);

        Assert.Contains(crew.Agents, a => a.Key == "writer");
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldRefuseTheEntity_WhenItIsWrittenUnderBothExtensions()
    {
        Write("both/config.yaml", "name: c\ngoal: g\n");
        Write("both/agents/a.yaml", "role: R\ngoal: G\n");
        Write("both/agents/a.yml", "role: R\ngoal: G\n");

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => NewLoader().LoadFromDirectoryAsync(VirtualRoot + "/both", TestContext.Current.CancellationToken));

        Assert.Contains("a.yaml", ex.Message);
        Assert.Contains("a.yml", ex.Message);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldFillEveryModelProperty_WhenWrittenInCamelOrSnakeCase()
    {
        Assert.NotEmpty(s_models);
        foreach (var model in s_models)
        {
            foreach (var property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.SetMethod is { IsPublic: true }))
            {
                foreach (var key in new[] { Camel(property.Name), Snake(Camel(property.Name)) }.Distinct())
                {
                    var faults = Faults(model, key + ":\n");
                    Assert.True(faults.Count == 0, $"{model.Name}.{property.Name} written as '{key}': {string.Join(" | ", faults)}");
                }
            }
        }

        var context = Faults(typeof(TaskYamlConfig), "context:\n  anyKey: 1\n  Another_Key: 2\n");
        Assert.Empty(context);
        var names = Faults(typeof(CrewYamlConfig), "agents:\n  Free_Name:\n    role: R\ntasks:\n  Any-Task:\n    description: D\n");
        Assert.Empty(names);

        foreach (var spelling in new[] { "TopK", "top_k", "topK" })
        {
            var knowledge = Faults(typeof(AgentYamlConfig), "knowledge:\n  - collection: docs\n    " + spelling + ": 3\n");
            Assert.True(knowledge.Count == 0, $"knowledge '{spelling}': {string.Join(" | ", knowledge)}");
        }
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldCarryTheCrewKeys_WhenComparingCrewSettingsToCrew()
    {
        var settings = typeof(CrewSettingsYamlConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var missing = typeof(CrewYamlConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.Name is not ("Agents" or "Tasks"))
            .Select(p => p.Name)
            .Where(name => !settings.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0, "CrewSettingsYamlConfig lacks: " + string.Join(", ", missing));
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldUseNoYamlMemberOrYamlIgnore_WhenAModelIsDeclared()
    {
        var offenders = s_models.Append(typeof(CrewSettingsYamlConfig))
            .SelectMany(m => m.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.GetCustomAttribute<YamlMemberAttribute>() is not null || p.GetCustomAttribute<YamlIgnoreAttribute>() is not null)
            .Select(p => p.DeclaringType!.Name + "." + p.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}
