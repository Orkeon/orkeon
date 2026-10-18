using System.Globalization;
using System.Reflection;
using System.Text;
using Orkeon.Application.Configuration;
using Orkeon.Infrastructure.Serialization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Reads a raw crew document the way the deserializer will, and refuses what it would drop or bend:
/// a key no model reads (a misspelled <c>temprature:</c> would vanish and the crew would run on a
/// default), a retired key, a duplicated key, a merge key, a node of another shape than its model
/// expects. Each fault names the file, the line, the path of the key and, for an unknown key, the
/// closest known key. A key counts as known the way <see cref="CamelOrSnakeCaseTypeInspector"/>
/// matches it: camelCase or snake_case, case-sensitive. An alias is judged where it is used, once.
/// </summary>
internal static class CrewYamlKeyCheck
{
    private const string MergeKey = "<<";

    private const string GraphConfigGuidance =
        "the crew-level circuit breaker bounded Graph mode only, where graphConfig carries the same settings — " +
        "on a process: graph crew, write graphConfig: { circuitBreakerPreset, maxTransitions, maxStateVisits, maxTotalDurationSeconds }; " +
        "other modes have no state machine to bound";

    private const string TaskBreakerGuidance =
        "the task state machine it configured never ran — a task is bounded by its agent's maxIter and by output-validation retries; " +
        "Graph-mode limits live in the crew's graphConfig block";

    private static readonly string[] s_llmNames = NamesOf(typeof(LlmYamlConfig));

    private static readonly string[] s_knowledgeNames = ["collection", "topK", "minScore", "profile", "maxContextTokens"];

    /// <summary>A key removed from a model: what replaces it.</summary>
    private sealed record RetiredKey(Type Model, string Name, string Guidance);

    private static readonly RetiredKey[] s_retired =
    [
        new(typeof(RagYamlConfig), "provider",
            "the document store is the host's choice — set Orkeon:Rag:Provider in the settings"),
        new(typeof(CrewYamlConfig), "circuitBreaker", GraphConfigGuidance),
        new(typeof(CrewSettingsYamlConfig), "circuitBreaker", GraphConfigGuidance),
        new(typeof(TaskYamlConfig), "circuitBreaker", TaskBreakerGuidance),
        new(typeof(TaskYamlConfig), "outputFile",
            "the framework writes a task's file from its deliverable: block — write deliverable: { path: /output/report.md }"),
    ];

    /// <summary>The faults of <paramref name="yaml"/>, one message each; empty when every key is read.</summary>
    /// <param name="yaml">The expanded YAML document.</param>
    /// <param name="file">The virtual path the document was read from, or null for a string.</param>
    /// <param name="model">The type the document deserializes to.</param>
    /// <param name="documentPath">
    /// Where the document sits in the crew document: empty for a whole crew (or its settings file),
    /// <c>tasks</c> for a flat <c>tasks.yaml</c>, <c>tasks.&lt;name&gt;</c> for one per-entity task file.
    /// </param>
    public static IReadOnlyList<string> Find(ExpandedYaml yaml, string? file, Type model, string documentPath)
    {
        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml.Text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        }
        catch (YamlException ex)
        {
            return [$"{Situate(file, yaml.FileLine(ex.Start.Line))}: {ex.Message}"];
        }

        // A document that is no block at all is the deserializer's to report.
        if (root is null or YamlScalarNode)
            return [];

        var context = new Context(yaml, file);
        WalkValue(root, model, PathOf(documentPath), context);
        return context.Faults;
    }

    /// <summary>Where a fault sits: the file and the line, or the line alone for a string.</summary>
    /// <param name="file">The virtual path the document was read from, or null for a string.</param>
    /// <param name="line">The line of the file.</param>
    public static string Situate(string? file, int line) => file is null
        ? string.Create(CultureInfo.InvariantCulture, $"line {line}")
        : string.Create(CultureInfo.InvariantCulture, $"{file}, line {line}");

    private static string PathOf(string documentPath)
    {
        var dot = documentPath.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? documentPath : $"{documentPath[..dot]}[{documentPath[(dot + 1)..]}]";
    }

    private sealed class Context(ExpandedYaml yaml, string? file)
    {
        private readonly Dictionary<YamlNode, HashSet<Type>> _judged = new(ReferenceEqualityComparer.Instance);

        public List<string> Faults { get; } = [];

        /// <summary>True the first time <paramref name="node"/> is judged as <paramref name="type"/>: an anchor serves many places, its faults are one.</summary>
        public bool FirstJudgement(YamlNode node, Type type)
        {
            if (!_judged.TryGetValue(node, out var types))
                _judged[node] = types = [];
            return types.Add(type);
        }

        public string Where(YamlNode node) => Situate(file, yaml.FileLine(node.Start.Line));
    }

    private static void WalkValue(YamlNode node, Type type, string path, Context context, string? hint = null)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        var isDictionary = IsDictionary(type, out var valueType);
        var isBlock = isDictionary || IsModel(type);

        if (node is YamlMappingNode mapping)
        {
            if (!isBlock)
            {
                if (IsCollection(type, out _))
                    context.Faults.Add(WrongShape(node, path, "a list", "a block of keys", hint, context));
                return;
            }

            if (!context.FirstJudgement(mapping, type))
                return;

            if (!isDictionary)
            {
                WalkModel(mapping, type, path, context);
                return;
            }

            foreach (var (key, value) in mapping.Children)
            {
                if (key is not YamlScalarNode { Value: { } name })
                    continue;

                if (name == MergeKey)
                    context.Faults.Add(MergeFault(context.Where(key), Entry(path, name)));
                else
                    WalkValue(value, valueType, Entry(path, name), context);
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            if (IsCollection(type, out var itemType))
            {
                for (var i = 0; i < sequence.Children.Count; i++)
                    WalkValue(sequence.Children[i], itemType, Entry(path, i.ToString(CultureInfo.InvariantCulture)), context);
            }
            else if (isBlock)
            {
                context.Faults.Add(WrongShape(node, path, "a block of keys", "a list", hint, context));
            }
        }
        else if (isBlock && node is YamlScalarNode scalar && !IsNull(scalar))
        {
            context.Faults.Add(WrongShape(node, path, "a block of keys", "a single value", hint, context));
        }
    }

    private static string WrongShape(YamlNode node, string path, string expected, string found, string? hint, Context context)
        => $"{context.Where(node)}: '{path}' must be {expected}, not {found}." + (hint is null ? string.Empty : " " + hint);

    private static string MergeFault(string where, string path)
        => $"{where}: '{path}' — a merge key (<<) is not supported: write the keys out, or share a text block under 'anchors:'.";

    private static void WalkModel(YamlMappingNode mapping, Type model, string path, Context context)
    {
        var names = NamesOf(model);
        foreach (var (keyNode, value) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key })
                continue;

            var keyPath = path.Length == 0 ? key : path + "." + key;
            if (key == MergeKey)
            {
                context.Faults.Add(MergeFault(context.Where(keyNode), keyPath));
                continue;
            }

            var property = model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.SetMethod is { IsPublic: true } && Matches(key, [CamelName(p.Name)]));
            if (property is null)
            {
                var retired = s_retired.FirstOrDefault(r => r.Model == model && Matches(key, [r.Name]));
                var where = context.Where(keyNode);
                context.Faults.Add(retired is not null
                    ? $"{where}: '{keyPath}' is retired: {retired.Guidance}."
                    : Describe(where, keyPath, key, names, model == typeof(AgentYamlConfig))
                      + (value.Anchor.IsEmpty ? string.Empty : " An anchor on an unknown key shares nothing: put shared text under 'anchors:'."));
                continue;
            }

            if (model == typeof(AgentYamlConfig) && property.Name == nameof(AgentYamlConfig.Knowledge))
                WalkKnowledge(value, keyPath, context);
            else
                WalkValue(value, property.PropertyType, keyPath, context, ShapeHint(model, property));
        }
    }

    private static string? ShapeHint(Type model, PropertyInfo property)
    {
        if (property.PropertyType == typeof(LlmYamlConfig))
            return "llm is a block of keys: write 'model: <name>' under it.";

        return model == typeof(TaskYamlConfig) && property.Name == nameof(TaskYamlConfig.Context)
            ? "context holds values for the prompt: to run a task after another, write 'dependencies:' with the task names."
            : null;
    }

    private static void WalkKnowledge(YamlNode node, string path, Context context)
    {
        if (node is not YamlSequenceNode sequence)
            return;

        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (sequence.Children[i] is not YamlMappingNode table)
                continue;

            foreach (var (keyNode, _) in table.Children)
            {
                if (keyNode is not YamlScalarNode { Value: { } key } || ReadByKnowledgeParser(key))
                    continue;

                context.Faults.Add(Describe(
                    context.Where(keyNode), Entry(path, i.ToString(CultureInfo.InvariantCulture)) + "." + key, key, s_knowledgeNames, false));
            }
        }
    }

    private static string Describe(string where, string path, string key, string[] names, bool isAgent)
    {
        var message = $"{where}: '{path}' is not a key Orkeon reads.";
        var snake = key.Contains('_', StringComparison.Ordinal);
        string[] known = [.. names.Select(n => snake ? SnakeName(n) : n)];

        if (isAgent && Matches(key, s_llmNames))
            return message + $" Did you mean 'llm.{key}'?";

        var sameLetters = known.FirstOrDefault(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase));
        if (sameLetters is not null)
            return message + $" Did you mean '{sameLetters}'? (keys are case-sensitive)";

        return ClosestName.Of(key, known) is { } closest
            ? message + $" Did you mean '{closest}'?"
            : message + $" It reads: {string.Join(", ", known)}.";
    }

    private static bool Matches(string key, string[] camelNames)
        => Array.IndexOf(camelNames, key) >= 0
           || Array.IndexOf(camelNames, CamelOrSnakeCaseTypeInspector.SnakeToCamelCase(key)) >= 0;

    // The knowledge parser (YamlCrewMapper.NormalizeKnowledgeKeys) lowercases a key and strips its underscores:
    // the check accepts exactly what it reads.
    private static bool ReadByKnowledgeParser(string key)
        => Array.Exists(s_knowledgeNames, name => string.Equals(
            name, key.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));

    private static string[] NamesOf(Type model)
        => [.. model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => CamelName(p.Name))];

    private static string CamelName(string property) => char.ToLowerInvariant(property[0]) + property[1..];

    private static string SnakeName(string camel)
    {
        var sb = new StringBuilder(camel.Length + 4);
        foreach (var c in camel)
        {
            if (char.IsUpper(c))
                sb.Append('_').Append(char.ToLowerInvariant(c));
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Entry(string path, string key) => path.Length == 0 ? key : $"{path}[{key}]";

    private static bool IsNull(YamlScalarNode scalar)
        => scalar.Style == ScalarStyle.Plain && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";

    private static bool IsModel(Type type)
        => type.IsClass && type.Assembly == typeof(CrewYamlConfig).Assembly;

    private static bool IsDictionary(Type type, out Type valueType)
    {
        var isDictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        valueType = isDictionary ? type.GetGenericArguments()[1] : typeof(object);
        return isDictionary;
    }

    private static bool IsCollection(Type type, out Type itemType)
    {
        var isCollection = type.IsGenericType && type.GetGenericArguments().Length == 1
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
        itemType = isCollection ? type.GetGenericArguments()[0] : typeof(object);
        return isCollection;
    }
}
